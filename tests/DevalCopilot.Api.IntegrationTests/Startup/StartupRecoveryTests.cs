using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Security.Bootstrap;
using DevalCopilot.Api.Startup;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;
using DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetRunningProcessAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetTerminalProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Startup;

/// <summary>
/// ADR-0072 acceptance D at the startup runner, over the real composition (side-effect-free, so nothing runs on its own) and a real
/// disposable SQLite file: a clean recovery, closed fatal diagnoses that stop every later step, coherent warnings, and the
/// preservation rule for terminal partial output.
/// </summary>
public sealed class StartupRecoveryTests : IDisposable
{
    private const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-startup-recovery-{Guid.NewGuid():N}");
    private readonly List<string> _databasePaths = [];

    public StartupRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task A_clean_recovery_on_a_fresh_database_is_neither_fatal_nor_warned()
    {
        using var host = HostOver(Path.Combine(_root, "clean.db"));

        var report = await RunAsync(host);

        Assert.Null(report.Failure);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task A_database_that_cannot_be_opened_is_storage_unavailable_at_the_storage_stage()
    {
        var directoryAsDatabase = Path.Combine(_root, "not-a-file.db");
        Directory.CreateDirectory(directoryAsDatabase);
        using var host = HostOver(directoryAsDatabase);

        var report = await RunAsync(host);

        Assert.Equal(new StartupFailure(StartupFailureStage.StorageSchema, StartupFailureReason.StorageUnavailable), report.Failure);
        Assert.Equal(StartupFailureAction.RestoreStorageAccess, report.Failure!.Action);
        Assert.Equal("DCSF1-StorageSchema-StorageUnavailable-RestoreStorageAccess", report.Failure.Code);
    }

    [Fact]
    public async Task A_file_that_is_not_a_database_is_incoherent_state_needing_reviewed_repair()
    {
        var garbage = Path.Combine(_root, "garbage.db");
        await File.WriteAllBytesAsync(garbage, Enumerable.Repeat((byte)0x5A, 8192).ToArray());
        using var host = HostOver(garbage);

        var report = await RunAsync(host);

        Assert.Equal(new StartupFailure(StartupFailureStage.StorageSchema, StartupFailureReason.StateIncoherent), report.Failure);
        Assert.Equal(StartupFailureAction.RequestReviewedRepair, report.Failure!.Action);
    }

    [Fact]
    public async Task A_failed_safety_critical_result_is_fatal_and_no_later_step_runs()
    {
        var later = new SpyPlanningInterruption();
        using var host = HostOver(Path.Combine(_root, "workspaces.db"), services =>
        {
            services.RemoveAll<IRequestHandler<ReconcileWorkspacesCommand, Result<int>>>();
            services.AddScoped<IRequestHandler<ReconcileWorkspacesCommand, Result<int>>>(_ => new FailingWorkspaceReconciliation());
            services.RemoveAll<IRequestHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>>();
            services.AddScoped<IRequestHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>>(_ => later);
        });

        var report = await RunAsync(host);

        Assert.Equal(new StartupFailure(StartupFailureStage.Workspaces, StartupFailureReason.StateIncoherent), report.Failure);
        Assert.Empty(report.Warnings);
        Assert.Equal(0, later.Calls);
    }

    [Fact]
    public async Task An_unclassified_exception_is_fatal_and_never_weaker()
    {
        using var host = HostOver(Path.Combine(_root, "agents.db"), services =>
        {
            services.RemoveAll<IRequestHandler<ReconcileInterruptedAgentAttemptsCommand, Result<int>>>();
            services.AddScoped<IRequestHandler<ReconcileInterruptedAgentAttemptsCommand, Result<int>>>(_ => new ThrowingAgentReconciliation());
        });

        var report = await RunAsync(host);

        Assert.Equal(new StartupFailure(StartupFailureStage.AgentAttempts, StartupFailureReason.Unclassified), report.Failure);
        Assert.Equal(StartupFailureAction.RequestReviewedRepair, report.Failure!.Action);
    }

    [Fact]
    public async Task Unclosed_previous_supervision_observations_are_a_warning_not_a_failure()
    {
        using var host = HostOver(Path.Combine(_root, "supervisions.db"), services =>
        {
            services.RemoveAll<IRequestHandler<CloseInterruptedAgentSupervisionsCommand, Result<CloseInterruptedAgentSupervisionsCommandResult>>>();
            services.AddScoped<IRequestHandler<CloseInterruptedAgentSupervisionsCommand, Result<CloseInterruptedAgentSupervisionsCommandResult>>>(
                _ => new PartiallyClosingSupervisions());
        });

        var report = await RunAsync(host);

        Assert.Null(report.Failure);
        Assert.Equal([StartupWarningCategory.SupervisionObservationsUnclosed], report.Warnings);
    }

    [Fact]
    public async Task Optional_output_failures_warn_and_only_a_proved_redundant_terminal_partial_is_removed()
    {
        var store = new ScriptedRecoveryStore();
        var running = new RunningProcessAttempt(Guid.NewGuid(), Guid.NewGuid());
        var redundant = new TerminalProcessAttempt(Guid.NewGuid(), Guid.NewGuid(), [ArtifactPurpose.ProcessStandardOutput]);
        var unrecorded = new TerminalProcessAttempt(Guid.NewGuid(), Guid.NewGuid(), []);
        using var host = HostOver(Path.Combine(_root, "outputs.db"), services =>
        {
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(store);
            services.RemoveAll<IRequestHandler<GetRunningProcessAttemptsQuery, IReadOnlyList<RunningProcessAttempt>>>();
            services.AddScoped<IRequestHandler<GetRunningProcessAttemptsQuery, IReadOnlyList<RunningProcessAttempt>>>(
                _ => new FixedQuery<GetRunningProcessAttemptsQuery, RunningProcessAttempt>([running]));
            services.RemoveAll<IRequestHandler<GetTerminalProcessAttemptsQuery, IReadOnlyList<TerminalProcessAttempt>>>();
            services.AddScoped<IRequestHandler<GetTerminalProcessAttemptsQuery, IReadOnlyList<TerminalProcessAttempt>>>(
                _ => new FixedQuery<GetTerminalProcessAttemptsQuery, TerminalProcessAttempt>([redundant, unrecorded]));
        });

        var report = await RunAsync(host);

        Assert.Null(report.Failure);
        Assert.Equal([StartupWarningCategory.OptionalOutputDeferred, StartupWarningCategory.RedundantCleanupDeferred], report.Warnings);

        // Only the stdout partial of the attempt that already recorded a stdout artifact was offered for removal; every partial of the
        // attempt with no recorded artifact, and the other purpose of the first, is preserved.
        Assert.Equal([(redundant.AttemptId, ArtifactPurpose.ProcessStandardOutput)], store.DeleteRequests);
    }

    [Fact]
    public async Task An_unreadable_historical_verification_output_is_a_warning_and_every_unproved_file_survives_repeated_starts()
    {
        // ADR-0072 (review WEP-R2): a real file-system fault on one verification output item, over the real composition and store.
        var store = new FilesystemArtifactStore(Path.Combine(_root, "artifacts"));
        using var host = HostOver(Path.Combine(_root, "verification-output.db"), services =>
        {
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(store);
            services.RemoveAll<IVerificationOutputArtifactStore>();
            services.AddSingleton<IVerificationOutputArtifactStore>(store);
        });
        Assert.Null((await RunAsync(host)).Failure);
        var (blocked, readable) = await SeedRunningVerificationsAsync(host);
        await WriteAsync(store.GetPartialPath(blocked, VerificationOutputPurpose.StandardOutput), "blocked sealed output");
        Assert.NotNull(await store.SealAsync(blocked, VerificationOutputPurpose.StandardOutput, CancellationToken.None));
        await WriteAsync(store.GetPartialPath(blocked, VerificationOutputPurpose.StandardError), "unsealed partial output");
        await WriteAsync(store.GetPartialPath(readable, VerificationOutputPurpose.StandardOutput), "readable sealed output");
        Assert.NotNull(await store.SealAsync(readable, VerificationOutputPurpose.StandardOutput, CancellationToken.None));
        var blockedSealed = Path.Combine(_root, "artifacts", (await store.DescribeSealedFileAsync(
            blocked, VerificationOutputPurpose.StandardOutput, CancellationToken.None))!.RelativeStoragePath);
        var blockedPartial = store.GetPartialPath(blocked, VerificationOutputPurpose.StandardError);

        StartupRecoveryReport first;
        await using (new FileStream(blockedSealed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            first = await RunAsync(host);
        }

        Assert.Null(first.Failure);
        Assert.Equal([StartupWarningCategory.OptionalOutputDeferred], first.Warnings);
        Assert.Equal([readable], await ImportedVerificationOutputsAsync(host));
        Assert.True(File.Exists(blockedSealed));
        Assert.Equal("unsealed partial output", await File.ReadAllTextAsync(blockedPartial));

        // Later starts: the unrecorded partial and the unimported sealed file are never deleted.
        var second = await RunAsync(host);
        var third = await RunAsync(host);
        Assert.Null(second.Failure);
        Assert.Null(third.Failure);
        Assert.True(File.Exists(blockedSealed));
        Assert.Equal("unsealed partial output", await File.ReadAllTextAsync(blockedPartial));
        Assert.Equal([readable], await ImportedVerificationOutputsAsync(host));
    }

    [Fact]
    public async Task A_database_fault_in_verification_output_recovery_is_fatal_and_no_later_step_runs()
    {
        var later = new SpyPlanningInterruption();
        using var host = HostOver(Path.Combine(_root, "verification-db-fault.db"), services =>
        {
            services.RemoveAll<IRequestHandler<RecoverInterruptedVerificationOutputArtifactsCommand, Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>>>();
            services.AddScoped<IRequestHandler<RecoverInterruptedVerificationOutputArtifactsCommand, Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>>>(
                _ => new DatabaseFaultingVerificationOutputRecovery());
            services.RemoveAll<IRequestHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>>();
            services.AddScoped<IRequestHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>>(_ => later);
        });

        var report = await RunAsync(host);

        Assert.Equal(new StartupFailure(StartupFailureStage.VerificationOutput, StartupFailureReason.StorageUnavailable), report.Failure);
        Assert.Empty(report.Warnings);
        Assert.Equal(0, later.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unclassified_historical_output_exception_is_fatal_for_both_attempt_families(bool agent)
    {
        // ADR-0072 (review WEP-R2): only a file-system fault on one item is optional; anything else is unclassified and fatal.
        var store = new ScriptedRecoveryStore(() => new InvalidOperationException("Scripted unclassified artifact failure."));
        using var host = HostOver(Path.Combine(_root, agent ? "agent-unclassified.db" : "process-unclassified.db"), services =>
        {
            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(store);
            if (agent)
            {
                services.RemoveAll<IRequestHandler<GetRunningAgentAttemptsQuery, IReadOnlyList<RunningAgentAttempt>>>();
                services.AddScoped<IRequestHandler<GetRunningAgentAttemptsQuery, IReadOnlyList<RunningAgentAttempt>>>(
                    _ => new FixedQuery<GetRunningAgentAttemptsQuery, RunningAgentAttempt>([new RunningAgentAttempt(Guid.NewGuid(), Guid.NewGuid())]));
            }
            else
            {
                services.RemoveAll<IRequestHandler<GetRunningProcessAttemptsQuery, IReadOnlyList<RunningProcessAttempt>>>();
                services.AddScoped<IRequestHandler<GetRunningProcessAttemptsQuery, IReadOnlyList<RunningProcessAttempt>>>(
                    _ => new FixedQuery<GetRunningProcessAttemptsQuery, RunningProcessAttempt>([new RunningProcessAttempt(Guid.NewGuid(), Guid.NewGuid())]));
            }
        });

        var report = await RunAsync(host);

        Assert.Equal(
            new StartupFailure(
                agent ? StartupFailureStage.AgentAttemptOutput : StartupFailureStage.ProcessAttemptOutput, StartupFailureReason.Unclassified),
            report.Failure);
        Assert.Empty(store.DeleteRequests);
    }

    [Fact]
    public void The_ready_marker_carries_only_closed_warning_names_and_the_failure_frame_only_closed_values()
    {
        Assert.Equal(
            "DEVALCOPILOT_SIDECAR_READY {\"port\":5123,\"warnings\":[\"OptionalOutputDeferred\",\"SupervisionObservationsUnclosed\"]}",
            BootstrapReadyMarker.Format(5123, [StartupWarningCategory.SupervisionObservationsUnclosed, StartupWarningCategory.OptionalOutputDeferred]));
        Assert.Equal("DEVALCOPILOT_SIDECAR_READY {\"port\":5123,\"warnings\":[]}", BootstrapReadyMarker.Format(5123));

        var failure = new StartupFailure(StartupFailureStage.LocalCommitOperations, StartupFailureReason.StateIncoherent);
        Assert.Equal(
            "DEVALCOPILOT_SIDECAR_FAILED {\"version\":1,\"stage\":\"LocalCommitOperations\",\"reason\":\"StateIncoherent\",\"action\":\"RequestReviewedRepair\"}",
            StartupFailureFrame.Format(failure));
        Assert.Equal("Startup failed: DCSF1-LocalCommitOperations-StateIncoherent-RequestReviewedRepair", StartupFailureFrame.FormatDiagnostic(failure));
        Assert.True(StartupFailureFrame.Format(failure).Length < 512);
    }

    public void Dispose()
    {
        // Only this class's own pools: ClearAllPools is process-wide and would invalidate another class's in-flight connection.
        foreach (var path in _databasePaths)
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Task<StartupRecoveryReport> RunAsync(WebApplicationFactory<Program> host) =>
        StartupRecovery.RunAsync(host.Services, NullLogger.Instance, CancellationToken.None);

    /// <summary>The real composition in the side-effect-free environment: no worker and no automatic recovery; only the runner under
    /// test touches the database.</summary>
    private WebApplicationFactory<Program> HostOver(string databasePath, Action<IServiceCollection>? configure = null)
    {
        _databasePaths.Add(databasePath);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "SideEffectFreeIntegrationTest");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={databasePath}"),
            ]));
            if (configure is not null)
            {
                builder.ConfigureTestServices(configure);
            }
        });
    }

    private static async Task WriteAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text);
    }

    private static async Task<(Guid Blocked, Guid Readable)> SeedRunningVerificationsAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var project = Project.Register(Guid.NewGuid(), $"Startup output {index}", $@"C:\repos\{Guid.NewGuid():N}", now);
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
            workspace.MarkReady();
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), new string('b', 64), []);
            var recipe = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\tools\verify.exe", ["test"], 60, true, now);
            db.Projects.Add(project);
            db.GitWorkspaces.Add(workspace);
            db.GitCheckpoints.Add(checkpoint);
            db.VerificationCommands.Add(recipe);
            db.RepositoryMutationLeases.Add(
                RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
            await db.SaveChangesAsync();
            var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, now);
            execution.MarkDispatched(now);
            db.VerificationExecutions.Add(execution);
            await db.SaveChangesAsync();
            ids.Add(execution.Id);
        }

        return (ids[0], ids[1]);
    }

    private static async Task<Guid[]> ImportedVerificationOutputsAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await db.VerificationOutputArtifacts.AsNoTracking().Select(artifact => artifact.VerificationExecutionId).ToArrayAsync();
    }

    /// <summary>A database fault inside the verification output recovery (a busy store), never an optional-output condition.</summary>
    private sealed class DatabaseFaultingVerificationOutputRecovery
        : ICommandHandler<RecoverInterruptedVerificationOutputArtifactsCommand, Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>>
    {
        public Task<Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>> HandleAsync(
            RecoverInterruptedVerificationOutputArtifactsCommand command, CancellationToken cancellationToken) =>
            throw new SqliteException("Scripted busy database.", 5);
    }

    private sealed class FailingWorkspaceReconciliation : ICommandHandler<ReconcileWorkspacesCommand, Result<int>>
    {
        public Task<Result<int>> HandleAsync(ReconcileWorkspacesCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result<int>.Failure(Error.Failure("test.failure", "Scripted failure.")));
    }

    private sealed class ThrowingAgentReconciliation : ICommandHandler<ReconcileInterruptedAgentAttemptsCommand, Result<int>>
    {
        public Task<Result<int>> HandleAsync(ReconcileInterruptedAgentAttemptsCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Scripted unclassified failure with a C:\\private\\path that must never be reported.");
    }

    private sealed class PartiallyClosingSupervisions
        : ICommandHandler<CloseInterruptedAgentSupervisionsCommand, Result<CloseInterruptedAgentSupervisionsCommandResult>>
    {
        public Task<Result<CloseInterruptedAgentSupervisionsCommandResult>> HandleAsync(
            CloseInterruptedAgentSupervisionsCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result<CloseInterruptedAgentSupervisionsCommandResult>.Success(new(ClosedCount: 2, UnclosedCount: 1)));
    }

    private sealed class SpyPlanningInterruption
        : ICommandHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>
    {
        public int Calls { get; private set; }

        public Task<Result<InterruptPlanningSequencesCommandResult>> HandleAsync(
            InterruptPlanningSequencesCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result<InterruptPlanningSequencesCommandResult>.Success(new(0)));
        }
    }

    private sealed class FixedQuery<TQuery, TItem>(IReadOnlyList<TItem> items) : IQueryHandler<TQuery, IReadOnlyList<TItem>>
        where TQuery : IQuery<IReadOnlyList<TItem>>
    {
        public Task<IReadOnlyList<TItem>> HandleAsync(TQuery query, CancellationToken cancellationToken) => Task.FromResult(items);
    }

    /// <summary>Every running stream fails to seal (a file-system fault unless scripted otherwise); every terminal stream has a
    /// partial file, and a removal never succeeds.</summary>
    private sealed class ScriptedRecoveryStore(Func<Exception>? sealFault = null) : IArtifactStore
    {
        public List<(Guid AttemptId, ArtifactPurpose Purpose)> DeleteRequests { get; } = [];

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            throw (sealFault?.Invoke() ?? new IOException("Scripted seal failure."));

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => true;

        public Task<SealedOutputFile?> DescribeSealedFileAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => DeleteRequests.Add((attemptId, purpose));

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
