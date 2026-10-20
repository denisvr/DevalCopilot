using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The final launch decision and the pre-dispatch SourceChanged recording over a real file-backed SQLite database. The expected
/// snapshot is what a supervisor read from the eligibility feed; a second connection changes the durable facts immediately before
/// the transaction begins, while the handler's own context stays alive and still tracks the old rows. Nothing may be marked or
/// recorded unless the fresh durable execution and its ownership agree with that snapshot.
/// </summary>
public sealed class VerificationDispatchDecisionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('b', 64);
    private static readonly string OtherFingerprint = new('c', 64);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public static TheoryData<string> DurableSnapshotChanges() => new()
    {
        "executable", "arguments reordered", "argument added", "timeout", "workspace path", "fingerprint", "recipe identity",
    };

    public static TheoryData<string> OwnershipChanges() => new()
    {
        "lease released", "workspace not ready", "newer workspace", "workspace path moved", "checkpoint fingerprint", "checkpoint moved",
    };

    // ---- The dispatch marker ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_agreeing_pending_execution_is_marked_once_and_a_second_decision_is_refused()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var first = await MarkAsync(dbContext, scene, scene.Expected, drift: null);
        var second = await MarkAsync(dbContext, scene, scene.Expected, drift: null);

        Assert.True(first.Result.IsSuccess);
        Assert.Equal(["begin"], first.Log);
        Assert.Equal("verification.execution_already_dispatched", Assert.Single(second.Result.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.NotNull((await verify.VerificationExecutions.SingleAsync()).DispatchedAtUtc);
    }

    [Theory]
    [MemberData(nameof(DurableSnapshotChanges))]
    public async Task A_durable_invocation_change_after_the_feed_never_authorizes_the_stale_tuple(string change)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, other => ChangeExecutionAsync(other, scene, change));

        Assert.Equal("verification.execution_snapshot_changed", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Theory]
    [MemberData(nameof(OwnershipChanges))]
    public async Task An_ownership_change_after_the_feed_refuses_the_dispatch(string change)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, other => ChangeOwnershipAsync(other, scene, change));

        Assert.Equal("verification.execution_not_current", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("arguments")]
    [InlineData("timeout")]
    [InlineData("path")]
    [InlineData("fingerprint")]
    [InlineData("workspace")]
    [InlineData("checkpoint")]
    [InlineData("command")]
    [InlineData("project")]
    public async Task An_expected_snapshot_that_disagrees_with_an_unchanged_durable_execution_is_refused(string field)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext, ["one", "two"]);
        var expected = field switch
        {
            "executable" => scene.Expected with { ExecutablePath = @"C:\other.exe" },
            "arguments" => scene.Expected with { Arguments = ["two", "one"] },
            "timeout" => scene.Expected with { TimeoutSeconds = scene.Expected.TimeoutSeconds + 1 },
            "path" => scene.Expected with { WorkspacePath = @"C:\elsewhere" },
            "fingerprint" => scene.Expected with { CheckpointFingerprintSha256 = OtherFingerprint },
            "workspace" => scene.Expected with { WorkspaceId = Guid.NewGuid() },
            "checkpoint" => scene.Expected with { CheckpointId = Guid.NewGuid() },
            "command" => scene.Expected with { VerificationCommandId = Guid.NewGuid() },
            _ => scene.Expected with { ProjectId = Guid.NewGuid() },
        };

        var outcome = await MarkAsync(dbContext, scene, expected, drift: null);

        Assert.Equal("verification.execution_snapshot_changed", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task A_terminal_execution_cannot_be_dispatched_and_an_unknown_one_is_not_found()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var terminal = await MarkAsync(dbContext, scene, scene.Expected, other =>
            other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Status, VerificationExecutionStatus.Interrupted)));
        var unknown = await new MarkVerificationExecutionDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(Guid.NewGuid(), scene.Expected), CancellationToken.None);

        Assert.Equal("verification.execution_not_dispatchable", Assert.Single(terminal.Result.Errors).Code);
        Assert.Equal("verification.execution_not_found", Assert.Single(unknown.Errors).Code);
    }

    [Fact]
    public async Task A_marker_committed_by_another_connection_is_seen_despite_the_stale_tracked_execution()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        Assert.Null((await dbContext.VerificationExecutions.SingleAsync()).DispatchedAtUtc); // tracked, about to become stale

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, other =>
            other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.DispatchedAtUtc, Now)));

        Assert.Equal("verification.execution_already_dispatched", Assert.Single(outcome.Result.Errors).Code);
    }

    [Fact]
    public async Task Concurrent_decisions_for_one_execution_mark_it_exactly_once()
    {
        await using var seed = fixture.CreateContext();
        var scene = await SeedAsync(seed);

        async Task<Result> DecideAsync()
        {
            await using var context = fixture.CreateContext();
            return await new MarkVerificationExecutionDispatchedCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
                new MarkVerificationExecutionDispatchedCommand(scene.ExecutionId, scene.Expected), CancellationToken.None);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(DecideAsync)));

        Assert.Single(results, result => result.IsSuccess);
        Assert.All(results.Where(result => result.IsFailure), result => Assert.Equal("verification.execution_already_dispatched", result.Errors[0].Code));
    }

    [Fact]
    public async Task A_failed_acquisition_commit_or_cancelled_decision_leaves_the_execution_pending()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => MarkAsync(
            dbContext, scene, scene.Expected, drift: null, configure: faulting => faulting.ThrowOnBeginTransaction = true));
        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => MarkAsync(
            dbContext, scene, scene.Expected, drift: null, configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit));
        await Assert.ThrowsAsync<OperationCanceledException>(() => MarkAsync(
            dbContext, scene, scene.Expected, drift: null, configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationBeforeCommit));

        await AssertPendingAsync(scene);
    }

    // ---- The pre-dispatch SourceChanged recording ----------------------------------------------------------------------

    [Fact]
    public async Task Genuine_drift_of_an_agreeing_pending_execution_is_recorded_without_a_process_outcome()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(dbContext, scene, scene.Expected, drift: null);

        Assert.True(outcome.IsSuccess);
        await using var verify = fixture.CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.SourceChanged, execution.Status);
        Assert.Equal(OtherFingerprint, execution.CompletionFingerprintSha256);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.NotNull(execution.CompletedAtUtc);
    }

    [Theory]
    [MemberData(nameof(DurableSnapshotChanges))]
    public async Task An_obsolete_observation_of_a_changed_durable_execution_records_nothing(string change)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(dbContext, scene, scene.Expected, other => ChangeExecutionAsync(other, scene, change));

        Assert.Equal("verification.execution_snapshot_changed", Assert.Single(outcome.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Theory]
    [MemberData(nameof(OwnershipChanges))]
    public async Task Ownership_loss_records_no_source_change(string change)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(dbContext, scene, scene.Expected, other => ChangeOwnershipAsync(other, scene, change));

        Assert.Equal("verification.execution_not_current", Assert.Single(outcome.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task A_dispatched_execution_can_no_longer_be_recorded_as_a_pre_dispatch_source_change()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(dbContext, scene, scene.Expected, other =>
            other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(execution => execution.DispatchedAtUtc, Now)));

        Assert.Equal("verification.execution_not_pending", Assert.Single(outcome.Errors).Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(VerificationExecutionStatus.Running, (await verify.VerificationExecutions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_failed_commit_records_no_source_change()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => SourceChangedAsync(
            dbContext, scene, scene.Expected, drift: null,
            configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit));

        await AssertPendingAsync(scene);
    }

    // ---- ADR-0057: nothing but this exact execution may own its workspace --------------------------------------------------

    public static TheoryData<string, string> CompetingOwners() => new()
    {
        { "agent attempt", "workspace_execution.agent_attempt_active" },
        { "undispatched agent attempt", "workspace_execution.agent_attempt_active" },
        { "second execution", "workspace_execution.verification_active" },
    };

    [Theory]
    [MemberData(nameof(CompetingOwners))]
    public async Task A_competing_owner_that_appears_after_the_feed_refuses_the_dispatch_and_leaves_the_execution_pending(string owner, string code)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, other => PlantOwnerAsync(other, scene, owner));

        Assert.Equal(code, Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Theory]
    [MemberData(nameof(CompetingOwners))]
    public async Task A_competing_owner_that_appears_after_the_feed_records_no_pre_dispatch_source_change(string owner, string code)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(dbContext, scene, scene.Expected, other => PlantOwnerAsync(other, scene, owner));

        Assert.Equal(code, Assert.Single(outcome.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task Terminal_owners_and_owners_of_another_repository_never_refuse_the_dispatch()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, async other =>
        {
            await PlantOwnerAsync(other, scene, "agent attempt");
            await other.Attempts.ExecuteUpdateAsync(set => set.SetProperty(attempt => attempt.Status, DevalCopilot.Domain.Features.Runs.AttemptStatus.Completed));
            await PlantOwnerAsync(other, scene, "second execution");
            await other.VerificationExecutions.Where(execution => execution.Id != scene.ExecutionId)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Status, VerificationExecutionStatus.Passed));
            await PlantOwnerAsync(other, scene, "other repository");
        });

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.NotNull((await verify.VerificationExecutions.SingleAsync(execution => execution.Id == scene.ExecutionId)).DispatchedAtUtc);
    }

    [Fact]
    public async Task Another_attempt_with_the_same_identifier_in_another_table_is_not_the_exact_execution_and_is_not_excluded()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, other =>
            PlantOwnerAsync(other, scene, "agent attempt", attemptId: scene.ExecutionId));

        Assert.Equal("workspace_execution.agent_attempt_active", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    private static async Task PlantOwnerAsync(DevalCopilotDbContext other, Scene scene, string owner, Guid? attemptId = null)
    {
        if (owner == "second execution")
        {
            var workspace = await other.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.Workspace.Id);
            var checkpoint = await other.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.CheckpointId);
            var recipe = await other.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.ProjectId == scene.ProjectId);
            other.VerificationExecutions.Add(VerificationExecution.Claim(Guid.NewGuid(), scene.ProjectId, 2, workspace, checkpoint, recipe, Now));
            await other.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var projectId = owner == "other repository"
            ? await other.Projects.AsNoTracking().Where(project => project.Id != scene.ProjectId).Select(project => project.Id).FirstAsync()
            : scene.ProjectId;
        var workspaceId = owner == "other repository" ? scene.OtherWorkspaceId : scene.Workspace.Id;
        var run = DevalCopilot.Domain.Features.Runs.Run.RecordIntent(Guid.NewGuid(), projectId, 1, "Objective", Now);
        run.Claim(Now);
        other.Runs.Add(run);
        await other.SaveChangesAsync(CancellationToken.None);
        var attempt = DevalCopilot.Domain.Features.Runs.Attempt.ClaimAgentImplementation(
            attemptId ?? Guid.NewGuid(), run.Id, 1, workspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048,
            Now, 1);
        if (owner != "undispatched agent attempt")
        {
            attempt.MarkAgentDispatched(Now);
        }

        other.Attempts.Add(attempt);
        await other.SaveChangesAsync(CancellationToken.None);
    }

    // ---- ADR-0058: at most one owner beside this exact execution anywhere in the database -----------------------------------

    [Theory]
    [InlineData(OwnerFamily.Agent, OwnerFamily.Verification)]
    [InlineData(OwnerFamily.Verification, OwnerFamily.LocalCommit)]
    [InlineData(OwnerFamily.LocalCommit, OwnerFamily.Agent)]
    public async Task Two_other_recorded_owners_anywhere_refuse_the_dispatch_and_leave_the_execution_pending(
        OwnerFamily first, OwnerFamily second)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, _ => ExecutionCapacitySeed.AddAsync(fixture, first, second));

        Assert.Equal("execution_capacity.limit_reached", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task One_other_recorded_owner_still_lets_the_exact_execution_dispatch()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(
            dbContext, scene, scene.Expected,
            _ => ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent));

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.NotNull((await verify.VerificationExecutions.SingleAsync(execution => execution.Id == scene.ExecutionId)).DispatchedAtUtc);
    }

    [Fact]
    public async Task Two_other_recorded_owners_also_refuse_the_pre_dispatch_source_change_that_shares_the_authority()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SourceChangedAsync(
            dbContext, scene, scene.Expected,
            _ => ExecutionCapacitySeed.AddAsync(
                fixture,
                OwnerFamily.Agent,
                OwnerFamily.Verification));

        Assert.Equal("execution_capacity.limit_reached", Assert.Single(outcome.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task An_Agent_attempt_with_the_executions_own_identifier_is_not_the_exact_execution_and_is_not_excluded()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, async other =>
        {
            var planted = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET Id = {scene.ExecutionId} WHERE Id = {planted.Id}");
            await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);
        });

        Assert.Equal("execution_capacity.limit_reached", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    [Fact]
    public async Task A_genuine_terminal_transition_of_another_owner_frees_the_place_for_a_fresh_dispatch()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        IReadOnlyList<PlacedOwner> placed = [];

        var refused = await MarkAsync(dbContext, scene, scene.Expected, async _ =>
            placed = await ExecutionCapacitySeed.AddAsync(
                fixture,
                OwnerFamily.Agent,
                OwnerFamily.Verification));
        Assert.Equal("execution_capacity.limit_reached", Assert.Single(refused.Result.Errors).Code);
        await AssertPendingAsync(scene);

        await ExecutionCapacitySeed.FinishAsync(fixture, placed[0]);
        await using var fresh = fixture.CreateContext();
        var admitted = await MarkAsync(fresh, scene, scene.Expected, drift: null);

        Assert.True(admitted.Result.IsSuccess);
    }

    [Fact]
    public async Task A_malformed_recorded_owner_refuses_the_dispatch_as_unavailable()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await MarkAsync(dbContext, scene, scene.Expected, async other =>
        {
            var planted = await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = {planted.Id}");
        });

        Assert.Equal("execution_capacity.unavailable", Assert.Single(outcome.Result.Errors).Code);
        await AssertPendingAsync(scene);
    }

    // ---- Harness ------------------------------------------------------------------------------------------------------

    private sealed record Scene(Guid ProjectId, Guid ExecutionId, Guid CheckpointId, GitWorkspace Workspace, Guid OtherWorkspaceId, VerificationDispatchSnapshot Expected);

    private sealed record DecisionOutcome(Result Result, List<string> Log);

    private async Task<DecisionOutcome> MarkAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        VerificationDispatchSnapshot expected,
        Func<DevalCopilotDbContext, Task>? drift,
        Action<FaultInjectingDbContext>? configure = null)
    {
        var log = new List<string>();
        var faulting = Faulting(dbContext, drift, log);
        configure?.Invoke(faulting);
        var result = await new MarkVerificationExecutionDispatchedCommandHandler(faulting, new FixedTimeProvider(Now)).HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(scene.ExecutionId, expected), CancellationToken.None);
        return new DecisionOutcome(result, log);
    }

    private async Task<Result> SourceChangedAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        VerificationDispatchSnapshot expected,
        Func<DevalCopilotDbContext, Task>? drift,
        Action<FaultInjectingDbContext>? configure = null)
    {
        var faulting = Faulting(dbContext, drift, []);
        configure?.Invoke(faulting);
        return await new RecordVerificationExecutionSourceChangedCommandHandler(faulting, new FixedTimeProvider(Now)).HandleAsync(
            new RecordVerificationExecutionSourceChangedCommand(scene.ExecutionId, OtherFingerprint, expected), CancellationToken.None);
    }

    private FaultInjectingDbContext Faulting(DevalCopilotDbContext dbContext, Func<DevalCopilotDbContext, Task>? drift, List<string> log) =>
        new(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                log.Add("begin");
                if (drift is not null)
                {
                    await using var other = fixture.CreateContext();
                    await drift(other);
                }
            },
        };

    private async Task AssertPendingAsync(Scene scene)
    {
        await using var verify = fixture.CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync(candidate => candidate.Id == scene.ExecutionId);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.CompletionFingerprintSha256);
        Assert.Null(execution.CompletedAtUtc);
    }

    private static async Task ChangeExecutionAsync(DevalCopilotDbContext other, Scene scene, string change)
    {
        var execution = await other.VerificationExecutions.AsNoTracking().SingleAsync();
        switch (change)
        {
            case "executable":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.ExecutablePath, @"C:\other.exe"));
                break;
            case "arguments reordered":
                var reversed = execution.Arguments.Reverse().ToArray();
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.Arguments, reversed));
                break;
            case "argument added":
                var extended = execution.Arguments.Append("--extra").ToArray();
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.Arguments, extended));
                break;
            case "timeout":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.TimeoutSeconds, 999));
                break;
            case "workspace path":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.WorkspacePath, @"C:\elsewhere"));
                break;
            case "fingerprint":
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.CheckpointFingerprintSha256, OtherFingerprint));
                break;
            case "recipe identity":
                var second = VerificationCommand.Configure(Guid.NewGuid(), scene.ProjectId, 2, "Second", @"C:\tools\verify.exe", ["x"], 60, true, Now);
                other.VerificationCommands.Add(second);
                await other.SaveChangesAsync(CancellationToken.None);
                await other.VerificationExecutions.ExecuteUpdateAsync(set => set.SetProperty(row => row.VerificationCommandId, second.Id));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    private static async Task ChangeOwnershipAsync(DevalCopilotDbContext other, Scene scene, string change)
    {
        switch (change)
        {
            case "lease released":
                await other.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
                break;
            case "workspace not ready":
                await other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention));
                break;
            case "newer workspace":
                var newer = ReadyWorkspace(scene.ProjectId, 2);
                other.GitWorkspaces.Add(newer);
                other.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                    Guid.NewGuid(), scene.ProjectId, newer.Id, 2, Guid.NewGuid().ToByteArray(), Now));
                await other.SaveChangesAsync(CancellationToken.None);
                break;
            case "workspace path moved":
                await other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.WorkspacePath, @"C:\workspaces\moved"));
                break;
            case "checkpoint fingerprint":
                await other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.CheckpointId)
                    .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.FingerprintSha256, OtherFingerprint));
                break;
            case "checkpoint moved":
                await other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.CheckpointId)
                    .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.WorkspaceId, scene.OtherWorkspaceId));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    private static GitWorkspace ReadyWorkspace(Guid projectId, int number)
    {
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), projectId, number, $@"C:\workspaces\{Guid.NewGuid():N}", $"branch-{number}", new string('a', 40), "main", Now);
        workspace.MarkReady();
        return workspace;
    }

    private static async Task<Scene> SeedAsync(DevalCopilotDbContext dbContext, string[]? arguments = null)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = ReadyWorkspace(project.Id, 1);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\tools\verify.exe", arguments ?? ["test", "--verbose"], 60, true, Now);
        var other = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = ReadyWorkspace(other.Id, 1);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, Now);

        dbContext.Projects.AddRange(project, other);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(recipe);
        dbContext.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), other.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Scene(project.Id, execution.Id, checkpoint.Id, workspace, otherWorkspace.Id, VerificationDispatchSnapshot.Of(execution));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
