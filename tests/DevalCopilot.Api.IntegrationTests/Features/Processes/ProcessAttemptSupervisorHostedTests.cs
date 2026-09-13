using System.Diagnostics;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Processes;

/// <summary>
/// Starts the real <see cref="ProcessAttemptSupervisor"/> — the actual <c>BackgroundService</c>,
/// with its real scoped mediator and EF-transaction pipeline (the same composition
/// <c>Program.cs</c> registers) and a real <see cref="ChildProcessExecutionAdapter"/> — against
/// a real fixture process, rather than driving its collaborators one call at a time.
/// </summary>
public sealed class ProcessAttemptSupervisorHostedTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _approvedRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-supervisor-hosted-root-{Guid.NewGuid():N}");
    private readonly string _markerPath;

    public ProcessAttemptSupervisorHostedTests()
    {
        Directory.CreateDirectory(_approvedRoot);
        _markerPath = Path.Combine(_approvedRoot, "executions.marker");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_approvedRoot))
        {
            Directory.Delete(_approvedRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Starting_the_supervisor_drives_one_claimed_attempt_to_a_terminal_state_exactly_once()
    {
        await using var provider = BuildServiceProvider();
        var (runId, attemptId) = await ClaimAttemptAsync(provider, ["append-marker", _markerPath], TimeSpan.FromSeconds(10));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Completed, status);

            // The supervisor polls every 200ms; several more ticks pass here while the
            // attempt sits in its terminal state, to prove it is never picked up again.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await AssertTerminalStateAsync(provider, runId, attemptId, AttemptStatus.Completed, RunLifecycle.Completed);

        var executionCount = (await File.ReadAllLinesAsync(_markerPath)).Length;
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task Host_cancellation_while_the_fixture_process_is_running_still_records_a_failed_cancelled_terminal_result()
    {
        await using var provider = BuildServiceProvider();
        var (runId, attemptId) = await ClaimAttemptAsync(provider, ["sleep-ms", "5000"], TimeSpan.FromSeconds(30));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);

        // Long enough for the supervisor to have claimed this attempt and for the real child
        // process to already be sleeping — the exact window the bug describes: a real
        // ProcessExecutionResult (Outcome = Cancelled, produced by the adapter's own
        // cancellation handling) exists, or is about to, when shutdown begins.
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        using (var stopCancellation = new CancellationTokenSource(PollTimeout))
        {
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await AssertTerminalStateAsync(provider, runId, attemptId, AttemptStatus.Failed, RunLifecycle.Failed);

        await using var verificationScope = provider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(ProcessOutcome.Cancelled, persistedAttempt!.ProcessOutcome);
        Assert.Null(persistedAttempt.ProcessExitCode);
    }

    [Fact]
    public async Task A_completed_result_is_durably_recorded_even_when_shutdown_begins_at_the_recording_boundary()
    {
        // Deterministically places graceful shutdown exactly at the narrow window the bug
        // describes — the real process has already exited with a real result, and the
        // recording dispatch is paused right at its own entry point — rather than hoping a
        // fixed delay lands there. The wrapper delegates to the real, unmodified handler once
        // released, so the actual recording transaction is what is proven durable.
        var boundaryReachedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildServiceProvider(services =>
        {
            services.RemoveAll<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>();
            services.AddScoped<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>(sp =>
                new RecordingBoundaryGateHandler(
                    new RecordProcessAttemptResultCommandHandler(
                        sp.GetRequiredService<IDevalCopilotDbContext>(), sp.GetRequiredService<TimeProvider>()),
                    boundaryReachedSource,
                    gateSource));
        });

        var (runId, attemptId) = await ClaimAttemptAsync(provider, ["exit-code", "0"], TimeSpan.FromSeconds(10));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);

        // Deterministic: waits for the supervisor to actually reach the recording boundary
        // (the real process has already exited) rather than guessing how long that takes.
        await boundaryReachedSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Graceful shutdown starts while the recording call is paused at the gate, then the
        // gate is released while shutdown is in progress — proving the production fix (the
        // recording dispatch's own bounded token, independent of stoppingToken) is what lets
        // the real handler still complete rather than losing this known result.
        using var stopCancellation = new CancellationTokenSource(PollTimeout);
        var stopTask = supervisor.StopAsync(stopCancellation.Token);
        gateSource.SetResult();

        await stopTask;

        await AssertTerminalStateAsync(provider, runId, attemptId, AttemptStatus.Completed, RunLifecycle.Completed);
    }

    [Fact]
    public async Task An_attempt_never_recorded_because_the_host_disappeared_before_recording_is_still_interrupted_on_restart()
    {
        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider())
        {
            (runId, attemptId) = await ClaimAttemptAsync(provider, ["sleep-ms", "800"], TimeSpan.FromSeconds(30));

            var supervisor = CreateSupervisor(provider);
            await supervisor.StartAsync(CancellationToken.None);

            // Long enough for the supervisor to have claimed the attempt and started the
            // real child process, but the provider is disposed directly below without ever
            // calling StopAsync — unlike a graceful shutdown (which this round's fix makes
            // durable), nothing here gets a chance to run the recording transaction at all,
            // the same as a genuine crash or host termination.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }

        // The abandoned real child process is still sleeping (started ~300ms into its 800ms
        // sleep) and still holds _approvedRoot as its working directory — outliving this
        // provider is exactly what is being proven, but the test's own directory cleanup in
        // Dispose() must not race it, so this waits out the remainder before moving on.
        await Task.Delay(TimeSpan.FromSeconds(1));

        SqliteConnection.ClearAllPools();

        // A fresh provider against the same database file, as a restarted host would open,
        // running only startup reconciliation — never the supervisor.
        await using var reopenedProvider = BuildServiceProvider();
        await using (var scope = reopenedProvider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var reconcileResult = await mediator.SendAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);
            Assert.True(reconcileResult.IsSuccess);
            Assert.Equal(1, reconcileResult.Value);
        }

        await AssertTerminalStateAsync(reopenedProvider, runId, attemptId, AttemptStatus.Interrupted, RunLifecycle.Interrupted);

        // Confirms this is genuinely the "dispatched but never recorded" scenario — the
        // supervisor did invoke the adapter for it — not merely "claimed and never touched".
        await using var verificationScope = reopenedProvider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await verificationDbContext.Attempts.FindAsync(attemptId);
        Assert.NotNull(persistedAttempt!.ProcessDispatchedAtUtc);
    }

    [Fact]
    public async Task Recording_uses_a_bounded_token_that_is_independent_of_the_host_stopping_token()
    {
        // A stand-in for a stalled local SQLite write: ignores stoppingToken entirely (the
        // supervisor is never asked to stop in this test) and only ever observes whatever
        // token the recording dispatch itself is given.
        var capturedTokenSource = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recordingCancelledSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildServiceProvider(services =>
        {
            // The mediator resolves a command's handler as IRequestHandler<TRequest,TResult>
            // (the base type ICommandHandler<,> extends) via GetServices<T>() and requires
            // exactly one — the real handler the earlier assembly scan registered under that
            // same type must be removed, not merely shadowed, or dispatch throws
            // MultipleRequestHandlersException before either handler ever runs.
            services.RemoveAll<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>();
            services.AddScoped<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>(
                _ => new StallingRecordProcessAttemptResultCommandHandler(capturedTokenSource, recordingCancelledSource));
        });

        var (_, attemptId) = await ClaimAttemptAsync(provider, ["exit-code", "0"], TimeSpan.FromSeconds(10));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            // Deterministic: awaits the actual handler invocation rather than sleeping a
            // guessed duration. WaitAsync's own bound is only a safety net against a truly
            // hung test, not the assertion itself.
            var recordingToken = await capturedTokenSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(CancellationToken.None, recordingToken);
            Assert.False(recordingToken.IsCancellationRequested);

            // stoppingToken is never cancelled anywhere in this test (StopAsync is only
            // called for cleanup, after this already completes) — so the recording token
            // cancelling on its own, within a bound well short of "forever", can only be its
            // own dedicated RecordingTimeout, not stoppingToken.
            var stopwatch = Stopwatch.StartNew();
            var recordingWasCancelled = await recordingCancelledSource.Task.WaitAsync(TimeSpan.FromSeconds(8));
            stopwatch.Stop();

            Assert.True(recordingWasCancelled);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                $"Expected the recording's own bounded token to cancel well under 8s; took {stopwatch.Elapsed}.");

            // No terminal result was ever produced by the stalled handler, so the attempt
            // must remain exactly as claimed — never left in some invented state.
            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
            Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
        }
        finally
        {
            // The attempt was durably marked dispatched before the adapter ever ran, so it is
            // never re-picked-up by a later poll even though recording itself stalled — only
            // the one recording dispatch above is ever in flight, already finished by now.
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task Recording_failure_does_not_cause_the_external_command_to_run_a_second_time()
    {
        var capturedTokenSource = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recordingCancelledSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildServiceProvider(services =>
        {
            services.RemoveAll<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>();
            services.AddScoped<IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>>(
                _ => new StallingRecordProcessAttemptResultCommandHandler(capturedTokenSource, recordingCancelledSource));
        });

        // append-marker records one line per real invocation of the external command — the
        // durable, out-of-process proof that it ran exactly once, rather than an in-memory
        // counter the supervisor itself could not be trusted to report honestly.
        var (_, attemptId) = await ClaimAttemptAsync(provider, ["append-marker", _markerPath], TimeSpan.FromSeconds(10));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await capturedTokenSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await recordingCancelledSource.Task.WaitAsync(TimeSpan.FromSeconds(8));

            // Several more poll ticks (every 200ms) pass here while the attempt sits
            // Running-but-dispatched, to prove it is never picked up — and the external
            // command never invoked — again.
            await Task.Delay(TimeSpan.FromMilliseconds(800));

            var executionCount = (await File.ReadAllLinesAsync(_markerPath)).Length;
            Assert.Equal(1, executionCount);

            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
            Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
            Assert.NotNull(persistedAttempt.ProcessDispatchedAtUtc);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task A_failed_dispatch_marking_transaction_prevents_the_adapter_from_running()
    {
        await using var provider = BuildServiceProvider(services =>
        {
            services.RemoveAll<IRequestHandler<MarkProcessAttemptDispatchedCommand, Result<DateTimeOffset>>>();
            services.AddScoped<IRequestHandler<MarkProcessAttemptDispatchedCommand, Result<DateTimeOffset>>>(
                _ => new AlwaysFailingMarkProcessAttemptDispatchedCommandHandler());
        });

        var (_, attemptId) = await ClaimAttemptAsync(provider, ["append-marker", _markerPath], TimeSpan.FromSeconds(10));

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            // Several poll ticks pass with no way to observe "the adapter was never called"
            // directly, so this simply waits out enough of them and then checks the durable
            // evidence: no marker line was ever written, and the attempt was never dispatched.
            await Task.Delay(TimeSpan.FromMilliseconds(800));

            Assert.False(File.Exists(_markerPath));

            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
            Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
            Assert.Null(persistedAttempt.ProcessDispatchedAtUtc);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    private ServiceProvider BuildServiceProvider(Action<ServiceCollection>? configureAdditionalServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProcessExecutionAdapter, ChildProcessExecutionAdapter>();
        services.AddDevalenteMediator(typeof(ClaimProcessAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(ClaimProcessAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        // Applied last so a test can replace a specific handler the assembly scan above
        // already registered (removing it first — see the one caller that does).
        configureAdditionalServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Test-only stand-in for a recording transaction that never completes on its own —
    /// proves the supervisor's recording dispatch is bounded by its own token rather than
    /// hanging forever or depending on <c>stoppingToken</c>.
    /// </summary>
    private sealed class StallingRecordProcessAttemptResultCommandHandler(
        TaskCompletionSource<CancellationToken> capturedTokenSource,
        TaskCompletionSource<bool> cancelledSource)
        : ICommandHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>
    {
        public async Task<Result<AttemptStatus>> HandleAsync(RecordProcessAttemptResultCommand command, CancellationToken cancellationToken)
        {
            capturedTokenSource.TrySetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelledSource.TrySetResult(true);
                throw;
            }

            throw new InvalidOperationException("Unreachable: the delay above never completes without cancellation.");
        }
    }

    /// <summary>
    /// Test-only wrapper around the real, unmodified <see cref="RecordProcessAttemptResultCommandHandler"/>:
    /// signals when the supervisor has reached the recording boundary, then waits on a
    /// test-controlled gate before delegating — letting a test place graceful shutdown at
    /// that exact point deterministically instead of guessing with a delay.
    /// </summary>
    private sealed class RecordingBoundaryGateHandler(
        RecordProcessAttemptResultCommandHandler realHandler,
        TaskCompletionSource boundaryReachedSource,
        TaskCompletionSource gateSource)
        : IRequestHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>
    {
        public async Task<Result<AttemptStatus>> HandleAsync(RecordProcessAttemptResultCommand command, CancellationToken cancellationToken)
        {
            boundaryReachedSource.TrySetResult();
            await gateSource.Task;
            return await realHandler.HandleAsync(command, cancellationToken);
        }
    }

    /// <summary>
    /// Test-only stand-in proving the adapter is never invoked when the execution-start claim
    /// transaction itself fails — always rejects, before any adapter call could happen.
    /// </summary>
    private sealed class AlwaysFailingMarkProcessAttemptDispatchedCommandHandler
        : ICommandHandler<MarkProcessAttemptDispatchedCommand, Result<DateTimeOffset>>
    {
        public Task<Result<DateTimeOffset>> HandleAsync(MarkProcessAttemptDispatchedCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result<DateTimeOffset>.Failure(Error.Conflict("attempts.dispatch_marking_test_failure", "Simulated failure.")));
    }

    private static ProcessAttemptSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IProcessExecutionAdapter>(),
        NullLogger<ProcessAttemptSupervisor>.Instance);

    private async Task<(Guid RunId, Guid AttemptId)> ClaimAttemptAsync(
        ServiceProvider provider, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Run through the real supervisor", DateTimeOffset.UtcNow);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();

        var intent = new ProcessExecutionIntent(
            ExecutablePath: FixtureExecutablePath,
            Arguments: arguments,
            WorkingDirectory: _approvedRoot,
            ApprovedRoot: _approvedRoot,
            Timeout: timeout,
            MaxBytesPerStream: ProcessExecutionRequest.DefaultMaxBytesPerStream,
            MaxTotalCapturedBytes: ProcessExecutionRequest.DefaultMaxTotalCapturedBytes);

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var claimResult = await mediator.SendAsync(new ClaimProcessAttemptCommand(run.Id, intent), CancellationToken.None);
        Assert.True(claimResult.IsSuccess);

        return (run.Id, claimResult.Value.AttemptId);
    }

    private static async Task<AttemptStatus> PollForTerminalStatusAsync(ServiceProvider provider, Guid attemptId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(PollTimeout);
        AttemptStatus status;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await using var pollScope = provider.CreateAsyncScope();
            var dbContext = pollScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            status = (await dbContext.Attempts.FindAsync(attemptId))!.Status;
        }
        while (status == AttemptStatus.Running && DateTimeOffset.UtcNow < deadline);

        return status;
    }

    private static async Task AssertTerminalStateAsync(
        ServiceProvider provider, Guid runId, Guid attemptId, AttemptStatus expectedAttemptStatus, RunLifecycle expectedRunLifecycle)
    {
        await PollForTerminalStatusAsync(provider, attemptId);

        await using var verificationScope = provider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        var persistedRun = await dbContext.Runs.FindAsync(runId);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(expectedAttemptStatus, persistedAttempt.Status);
        Assert.NotNull(persistedRun);
        Assert.Equal(expectedRunLifecycle, persistedRun.Lifecycle);
    }
}
