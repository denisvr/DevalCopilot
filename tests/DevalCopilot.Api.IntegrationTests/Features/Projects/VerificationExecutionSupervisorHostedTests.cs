using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Devalente.Shared.Cqrs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// The real verification supervisor, mediator, EF pipeline, file-backed SQLite and sealed output store, with only the process
/// boundary and the Git observation as deterministic doubles. The eligibility feed supplies candidates, never authority: the
/// supervisor launches only after a fresh single-use dispatch decision agrees with the durable execution and its ownership, so
/// an authority or snapshot change after the feed (here applied while the supervisor's own Git observation runs) must launch
/// zero processes and leave the claim pending, never fabricate a fingerprint or process outcome, and never launch twice.
/// </summary>
public sealed class VerificationExecutionSupervisorHostedTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Head = new('a', 40);
    private static readonly string Fingerprint = new('b', 64);
    private static readonly string DriftedFingerprint = new('c', 64);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-verify-hosted-{Guid.NewGuid():N}.db");
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-verify-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore artifactStore;

    public VerificationExecutionSupervisorHostedTests()
    {
        artifactStore = new FilesystemArtifactStore(artifactRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (Directory.Exists(artifactRoot))
        {
            Directory.Delete(artifactRoot, recursive: true);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    // ---- Success -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_current_execution_launches_exactly_once_with_the_claimed_invocation_in_order_and_seals_its_output()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["one", "two", "--three"], timeoutSeconds: 77);
        host.Process.Results.Enqueue((0, "stdout text", "stderr text"));

        await RunUntilTerminalAsync(host, scene.ExecutionId);

        var request = Assert.Single(host.Process.Requests);
        Assert.Equal(scene.Recipe.ExecutablePath, request.ExecutablePath);
        Assert.Equal(["one", "two", "--three"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(77), request.Timeout);
        Assert.Equal(scene.Workspace.WorkspacePath, request.WorkingDirectory);
        Assert.Equal(scene.Workspace.WorkspacePath, request.ApprovedRoot);
        await using var verify = CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, execution.Outcome);
        Assert.Equal(0, execution.ExitCode);
        Assert.NotNull(execution.DispatchedAtUtc);
        Assert.Equal(2, await verify.VerificationOutputArtifacts.CountAsync(artifact => artifact.VerificationExecutionId == execution.Id));
    }

    [Fact]
    public async Task A_live_recipe_edit_after_the_claim_still_runs_the_original_snapshot()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["original"], timeoutSeconds: 60);
        await using (var other = CreateContext())
        {
            var recipe = await other.VerificationCommands.SingleAsync();
            recipe.Update("Edited", @"C:\edited.exe", ["changed", "arguments"], 5, false, Now.AddMinutes(1));
            await other.SaveChangesAsync(CancellationToken.None);
        }

        await RunUntilTerminalAsync(host, scene.ExecutionId);

        var request = Assert.Single(host.Process.Requests);
        Assert.Equal(scene.Recipe.ExecutablePath, request.ExecutablePath);
        Assert.Equal(["original"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
        await using var verify = CreateContext();
        Assert.Equal(VerificationExecutionStatus.Passed, (await verify.VerificationExecutions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_claim_made_through_the_real_mediator_is_launched_once_from_its_own_snapshot()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["claimed"], timeoutSeconds: 60, claim: false);
        await using var scope = host.Provider.CreateAsyncScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ClaimVerificationExecutionCommand(scene.ProjectId, scene.Recipe.Id, scene.CheckpointId), CancellationToken.None);
        Assert.True(claimed.IsSuccess);

        await RunUntilTerminalAsync(host, claimed.Value.VerificationExecutionId);

        Assert.Equal(["claimed"], Assert.Single(host.Process.Requests).Arguments);
    }

    // ---- Refusals: zero process calls, the claim stays pending ---------------------------------------------------------

    public static TheoryData<string> AuthorityAndSnapshotChanges() => new()
    {
        "lease released",
        "workspace not ready",
        "newer workspace",
        "workspace path moved",
        "checkpoint fingerprint changed",
        "checkpoint moved to another workspace",
        "executable changed",
        "arguments reordered",
        "argument added",
        "timeout changed",
        "execution fingerprint changed",
        "execution recipe identity changed",
    };

    [Theory]
    [MemberData(nameof(AuthorityAndSnapshotChanges))]
    public async Task A_change_after_the_feed_and_during_the_observation_launches_nothing_and_leaves_the_claim_pending(string change)
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["first", "second"], timeoutSeconds: 60);
        host.Evidence.OnCapture = ApplyOnce(() => ApplyChangeAsync(scene, change));

        await RunForAWhileAsync(host);

        Assert.Empty(host.Process.Requests);
        await AssertStillPendingAsync(scene.ExecutionId);
    }

    [Fact]
    public async Task A_refused_observation_launches_nothing_and_marks_nothing()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["x"], timeoutSeconds: 60);
        host.Evidence.Outcome = GitWorkspaceEvidenceOutcome.GitInvocationFailed;

        await RunForAWhileAsync(host);

        Assert.Empty(host.Process.Requests);
        await AssertStillPendingAsync(scene.ExecutionId);
    }

    // ---- SourceChanged ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Genuine_physical_drift_is_recorded_as_source_changed_without_a_process_or_an_invented_outcome()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["x"], timeoutSeconds: 60);
        host.Evidence.FingerprintSha256 = DriftedFingerprint;

        await RunUntilTerminalAsync(host, scene.ExecutionId);

        Assert.Empty(host.Process.Requests);
        await using var verify = CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.SourceChanged, execution.Status);
        Assert.Equal(DriftedFingerprint, execution.CompletionFingerprintSha256);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.NotNull(execution.CompletedAtUtc);
    }

    [Theory]
    [InlineData("lease released")]
    [InlineData("newer workspace")]
    [InlineData("workspace path moved")]
    [InlineData("executable changed")]
    [InlineData("arguments reordered")]
    [InlineData("execution fingerprint changed")]
    public async Task An_obsolete_drift_observation_records_nothing_for_an_execution_it_did_not_describe(string change)
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["first", "second"], timeoutSeconds: 60);
        host.Evidence.FingerprintSha256 = DriftedFingerprint;
        host.Evidence.OnCapture = ApplyOnce(() => ApplyChangeAsync(scene, change));

        await RunForAWhileAsync(host);

        Assert.Empty(host.Process.Requests);
        await AssertStillPendingAsync(scene.ExecutionId);
    }

    // ---- Single use and restart --------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_supervisors_racing_for_the_same_execution_launch_it_once()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["race"], timeoutSeconds: 60);
        host.Process.Delay = TimeSpan.FromMilliseconds(300);
        using var gate = new Barrier(2);
        host.Evidence.OnCapture = async (number, _) =>
        {
            if (number <= 2)
            {
                await Task.Run(() => gate.SignalAndWait(TimeSpan.FromSeconds(5)));
            }
        };
        var first = SupervisorFor(host);
        var second = SupervisorFor(host);

        await Task.WhenAll(first.StartAsync(CancellationToken.None), second.StartAsync(CancellationToken.None));
        try
        {
            await WaitUntilAsync(host, scene.ExecutionId, status => status != VerificationExecutionStatus.Running);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(first.StopAsync(stop.Token), second.StopAsync(stop.Token));
        }

        Assert.Single(host.Process.Requests);
    }

    [Fact]
    public async Task A_dispatched_but_unrecorded_execution_is_never_launched_again_and_reconciles_as_interrupted()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["x"], timeoutSeconds: 60);
        await using (var other = CreateContext())
        {
            await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.DispatchedAtUtc, Now));
        }

        await RunForAWhileAsync(host);
        Assert.Empty(host.Process.Requests);

        await using var scope = host.Provider.CreateAsyncScope();
        var reconciled = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ReconcileInterruptedVerificationExecutionsCommand(), CancellationToken.None);
        Assert.Equal(1, reconciled.Value);
        await using var verify = CreateContext();
        Assert.Equal(VerificationExecutionStatus.Interrupted, (await verify.VerificationExecutions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_process_failure_after_dispatch_still_records_a_failed_outcome_after_one_call()
    {
        await using var host = await BuildHostAsync();
        var scene = await SeedClaimAsync(host, ["x"], timeoutSeconds: 60);
        host.Process.Throw = true;

        await RunUntilTerminalAsync(host, scene.ExecutionId);

        Assert.Single(host.Process.Requests);
        await using var verify = CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.Failed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Failed, execution.Outcome);
    }

    // ---- Harness ------------------------------------------------------------------------------------------------------

    private sealed record Scene(
        Guid ProjectId, Guid ExecutionId, Guid CheckpointId, VerificationCommand Recipe, GitWorkspace Workspace, Guid OtherWorkspaceId);

    private async Task ApplyChangeAsync(Scene scene, string change)
    {
        await using var other = CreateContext();
        switch (change)
        {
            case "lease released":
                await other.RepositoryMutationLeases.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
                break;
            case "workspace not ready":
                await other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention));
                break;
            case "newer workspace":
                var project = await other.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
                var newer = ReadyWorkspace(project.Id, 2);
                other.GitWorkspaces.Add(newer);
                other.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                    Guid.NewGuid(), project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), Now));
                await other.SaveChangesAsync(CancellationToken.None);
                break;
            case "workspace path moved":
                await other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.WorkspacePath, @"C:\workspaces\moved"));
                break;
            case "checkpoint fingerprint changed":
                await other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.CheckpointId)
                    .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.FingerprintSha256, DriftedFingerprint));
                break;
            case "checkpoint moved to another workspace":
                await other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.CheckpointId)
                    .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.WorkspaceId, scene.OtherWorkspaceId));
                break;
            case "executable changed":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ExecutablePath, @"C:\other.exe"));
                break;
            case "arguments reordered":
                var reorder = await other.VerificationExecutions.SingleAsync();
                var arguments = reorder.Arguments.Reverse().ToArray();
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Arguments, arguments));
                break;
            case "argument added":
                var added = await other.VerificationExecutions.SingleAsync();
                var extended = added.Arguments.Append("--extra").ToArray();
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Arguments, extended));
                break;
            case "timeout changed":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.TimeoutSeconds, 999));
                break;
            case "execution fingerprint changed":
                await other.VerificationExecutions.ExecuteUpdateAsync(
                    set => set.SetProperty(execution => execution.CheckpointFingerprintSha256, DriftedFingerprint));
                break;
            case "execution recipe identity changed":
                var second = VerificationCommand.Configure(Guid.NewGuid(), scene.ProjectId, 2, "Second", scene.Recipe.ExecutablePath, ["x"], 60, true, Now);
                other.VerificationCommands.Add(second);
                await other.SaveChangesAsync(CancellationToken.None);
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.VerificationCommandId, second.Id));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    private async Task AssertStillPendingAsync(Guid executionId)
    {
        await using var verify = CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync(candidate => candidate.Id == executionId);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.CompletionFingerprintSha256);
        Assert.Null(execution.CompletedAtUtc);
        Assert.Empty(await verify.VerificationOutputArtifacts.ToListAsync());
    }

    /// <summary>Applies the change during the first observation only and then holds every later observation until the
    /// supervisor stops, so the test judges exactly one launch decision: a later poll that reads the changed row afresh would
    /// legitimately agree with it, which is not the stale-feed case under test.</summary>
    private static Func<int, CancellationToken, Task> ApplyOnce(Func<Task> change)
    {
        var applied = 0;
        return async (_, cancellationToken) =>
        {
            if (Interlocked.Exchange(ref applied, 1) == 0)
            {
                await change();
            }
            else
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        };
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private static GitWorkspace ReadyWorkspace(Guid projectId, int number)
    {
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), projectId, number, $@"C:\workspaces\{Guid.NewGuid():N}", $"branch-{number}", Head, "main", Now);
        workspace.MarkReady();
        return workspace;
    }

    private async Task<Scene> SeedClaimAsync(Host host, string[] arguments, int timeoutSeconds, bool claim = true)
    {
        await using var db = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = ReadyWorkspace(project.Id, 1);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Head, Fingerprint, []);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\tools\verify.exe", arguments, timeoutSeconds, true, Now);
        var other = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = ReadyWorkspace(other.Id, 1);
        db.Projects.AddRange(project, other);
        db.GitWorkspaces.AddRange(workspace, otherWorkspace);
        db.GitCheckpoints.Add(checkpoint);
        db.VerificationCommands.Add(recipe);
        db.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), other.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        var executionId = Guid.Empty;
        if (claim)
        {
            var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, Now);
            db.VerificationExecutions.Add(execution);
            executionId = execution.Id;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return new Scene(project.Id, executionId, checkpoint.Id, recipe, workspace, otherWorkspace.Id);
    }

    private sealed class ScriptedEvidence : IGitWorkspaceEvidenceReader
    {
        public string FingerprintSha256 { get; set; } = Fingerprint;

        public GitWorkspaceEvidenceOutcome Outcome { get; set; } = GitWorkspaceEvidenceOutcome.Success;

        public Func<int, CancellationToken, Task>? OnCapture { get; set; }

        private int captures;

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref captures);
            if (OnCapture is not null)
            {
                await OnCapture(number, cancellationToken);
            }

            return Outcome == GitWorkspaceEvidenceOutcome.Success
                ? new GitWorkspaceEvidenceResult(Outcome, Head, FingerprintSha256, [], null)
                : new GitWorkspaceEvidenceResult(Outcome, null, null, [], null);
        }
    }

    private sealed class RecordingProcess : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Queue<(int ExitCode, string Stdout, string Stderr)> Results { get; } = new();

        public TimeSpan Delay { get; set; }

        public bool Throw { get; set; }

        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Throw)
            {
                throw new InvalidOperationException("The process could not be started.");
            }

            var (exitCode, stdout, stderr) = Results.Count > 0 ? Results.Dequeue() : (0, string.Empty, string.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputSinkPath!)!);
            await File.WriteAllTextAsync(request.StandardOutputSinkPath!, stdout, cancellationToken);
            await File.WriteAllTextAsync(request.StandardErrorSinkPath!, stderr, cancellationToken);
            return new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = exitCode,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromMilliseconds(5),
            };
        }
    }

    private sealed record Host(ServiceProvider Provider, ScriptedEvidence Evidence, RecordingProcess Process) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private async Task<Host> BuildHostAsync()
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var evidence = new ScriptedEvidence();
        var process = new RecordingProcess();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IVerificationOutputArtifactStore>(artifactStore);
        services.AddSingleton<IProcessExecutionAdapter>(process);
        services.AddDevalenteMediator(typeof(ClaimVerificationExecutionCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(ClaimVerificationExecutionCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return new Host(services.BuildServiceProvider(), evidence, process);
    }

    private static VerificationExecutionSupervisor SupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Process,
        host.Evidence,
        host.Provider.GetRequiredService<IVerificationOutputArtifactStore>(),
        NullLogger<VerificationExecutionSupervisor>.Instance);

    private async Task WaitUntilAsync(Host host, Guid executionId, Func<VerificationExecutionStatus, bool> done)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(40);
            await using var db = CreateContext();
            if (done((await db.VerificationExecutions.AsNoTracking().SingleAsync(execution => execution.Id == executionId)).Status))
            {
                return;
            }
        }

        throw new TimeoutException("The verification execution did not reach the expected state.");
    }

    private async Task RunUntilTerminalAsync(Host host, Guid executionId)
    {
        var supervisor = SupervisorFor(host);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(host, executionId, status => status != VerificationExecutionStatus.Running);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await supervisor.StopAsync(stop.Token);
        }
    }

    private static async Task RunForAWhileAsync(Host host)
    {
        var supervisor = SupervisorFor(host);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await supervisor.StopAsync(stop.Token);
        }
    }
}
