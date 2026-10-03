using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The explicit verification claim's commit seam over a real file-backed SQLite database. Every scenario keeps the handler's own
/// context alive and populated with the tracked entities it seeded (including a stale Project and its counter), then lets a second
/// connection commit a change either while the external Git observation is running or immediately before the transaction begins.
/// A tracked entity is never fresh authority: a changed fact must be refused (or, for the counter, read afresh), and a refusal must
/// leave no execution and no consumed execution number.
/// </summary>
public sealed class ClaimVerificationExecutionCommitSeamTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('b', 64);
    private static readonly string OtherFingerprint = new('c', 64);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public enum Moment
    {
        DuringCapture,
        BeforeBegin,
    }

    // ---- Recipe drift ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_recipe_disabled_after_the_reads_is_refused(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.VerificationCommands.Where(recipe => recipe.Id == scene.Unit.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(recipe => recipe.IsEnabled, false)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.disabled");
    }

    [Theory]
    [InlineData(Moment.DuringCapture, "name")]
    [InlineData(Moment.DuringCapture, "executable")]
    [InlineData(Moment.DuringCapture, "timeout")]
    [InlineData(Moment.DuringCapture, "arguments")]
    [InlineData(Moment.BeforeBegin, "name")]
    [InlineData(Moment.BeforeBegin, "executable")]
    [InlineData(Moment.BeforeBegin, "timeout")]
    [InlineData(Moment.BeforeBegin, "arguments")]
    public async Task A_recipe_whose_invocation_changed_after_the_reads_is_never_silently_substituted(Moment moment, string field)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, async other =>
        {
            var recipe = await other.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Unit.Id);
            recipe.Update(
                field == "name" ? "Renamed" : recipe.Name,
                field == "executable" ? @"C:\other.exe" : recipe.ExecutablePath,
                field == "arguments" ? ["test", "--filter", "x"] : recipe.Arguments.ToArray(),
                field == "timeout" ? 61 : recipe.TimeoutSeconds,
                recipe.IsEnabled,
                Now.AddMinutes(1));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(scene, outcome.Result, "verification.recipe_changed");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task Reordered_arguments_are_a_changed_recipe(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext, ["one", "two"]);

        var outcome = await ClaimAsync(dbContext, scene, moment, async other =>
        {
            var recipe = await other.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Unit.Id);
            recipe.Update(recipe.Name, recipe.ExecutablePath, ["two", "one"], recipe.TimeoutSeconds, true, Now.AddMinutes(1));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(scene, outcome.Result, "verification.recipe_changed");
    }

    // ---- Workspace and checkpoint drift ----------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_newer_workspace_committed_after_the_reads_is_not_silently_targeted(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, async other =>
        {
            var newer = ReadyWorkspace(scene.Project, 2);
            other.GitWorkspaces.Add(newer);
            other.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), scene.Project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), Now));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(scene, outcome.Result, "verification.not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_workspace_that_stopped_being_ready_after_the_reads_is_refused(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.workspace_not_ready");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_workspace_path_changed_after_the_reads_is_not_the_path_that_was_captured(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.WorkspacePath, @"C:\workspaces\moved")));

        await AssertRefusedAsync(scene, outcome.Result, "verification.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_lease_lost_after_the_reads_is_refused(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.workspace_not_ready");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_checkpoint_fingerprint_changed_after_the_reads_is_not_the_observed_source(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.Checkpoint.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.FingerprintSha256, OtherFingerprint)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_checkpoint_number_changed_after_the_reads_is_not_the_observed_source(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.Checkpoint.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.CheckpointNumber, 9)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_checkpoint_that_moved_to_another_workspace_after_the_reads_is_refused(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.Checkpoint.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.WorkspaceId, scene.OtherWorkspace.Id)));

        await AssertRefusedAsync(scene, outcome.Result, "verification.not_found");
    }

    // ---- A competing running execution and the counter -------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_running_execution_committed_after_the_reads_by_another_recipe_blocks_the_claim(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, async other =>
        {
            other.VerificationExecutions.Add(VerificationExecution.Claim(
                Guid.NewGuid(), scene.Project.Id, 7, scene.Workspace, scene.Checkpoint, scene.Lint, Now));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(scene, outcome.Result, "verification.already_running", expectedExecutions: 1);
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task The_execution_number_is_reserved_from_the_fresh_counter_not_the_stale_tracked_project(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        Assert.Equal(1, scene.Project.NextVerificationExecutionNumber); // the tracked Project is about to become stale

        var outcome = await ClaimAsync(dbContext, scene, moment, other =>
            other.Projects.Where(project => project.Id == scene.Project.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(project => project.NextVerificationExecutionNumber, 5)));

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(5, outcome.Result.Value.ExecutionNumber);
        await using var verify = fixture.CreateContext();
        Assert.Equal(6, await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
        Assert.Equal(5, (await verify.VerificationExecutions.SingleAsync()).ExecutionNumber);
    }

    [Fact]
    public async Task A_later_save_of_the_same_stale_context_does_not_overwrite_the_reserved_counter()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    [Fact]
    public async Task A_refusal_consumes_no_execution_number()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var refused = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, other =>
            other.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released)));
        await AssertRefusedAsync(scene, refused.Result, "verification.workspace_not_ready");

        await using (var restore = fixture.CreateContext())
        {
            await restore.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Active));
        }

        var accepted = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);
        Assert.True(accepted.Result.IsSuccess);
        Assert.Equal(1, accepted.Result.Value.ExecutionNumber);
    }

    // ---- Positive controls ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_unchanged_claim_snapshots_the_fresh_recipe_workspace_and_checkpoint(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, moment, drift: null);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(1, outcome.Result.Value.ExecutionNumber);
        await using var verify = fixture.CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.Equal(scene.Workspace.Id, execution.GitWorkspaceId);
        Assert.Equal(scene.Workspace.WorkspacePath, execution.WorkspacePath);
        Assert.Equal(scene.Checkpoint.Id, execution.GitCheckpointId);
        Assert.Equal(Fingerprint, execution.CheckpointFingerprintSha256);
        Assert.Equal(scene.Unit.Id, execution.VerificationCommandId);
        Assert.Equal(scene.Unit.Name, execution.CommandName);
        Assert.Equal(scene.Unit.ExecutablePath, execution.ExecutablePath);
        Assert.Equal(scene.Unit.Arguments, execution.Arguments);
        Assert.Equal(scene.Unit.TimeoutSeconds, execution.TimeoutSeconds);
    }

    [Fact]
    public async Task An_older_selected_checkpoint_whose_fingerprint_still_matches_remains_eligible()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), scene.Workspace.Id, 2, Now.AddMinutes(1), new string('a', 40), OtherFingerprint, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var outcome = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.Equal(scene.Checkpoint.Id, (await verify.VerificationExecutions.SingleAsync()).GitCheckpointId);
    }

    [Fact]
    public async Task A_historical_running_execution_in_an_older_workspace_does_not_block_the_current_workspace()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var current = ReadyWorkspace(scene.Project, 2);
        var currentCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), current.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitWorkspaces.Add(current);
        dbContext.GitCheckpoints.Add(currentCheckpoint);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), scene.Project.Id, current.Id, 2, Guid.NewGuid().ToByteArray(), Now));
        dbContext.VerificationExecutions.Add(VerificationExecution.Claim(
            Guid.NewGuid(), scene.Project.Id, 3, scene.Workspace, scene.Checkpoint, scene.Lint, Now)); // Running in the older workspace
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var outcome = await ClaimAsync(dbContext, scene with { Checkpoint = currentCheckpoint, Workspace = current }, Moment.BeforeBegin, drift: null);

        Assert.True(outcome.Result.IsSuccess);
    }

    [Fact]
    public async Task A_live_recipe_edit_after_the_claim_never_changes_the_claimed_snapshot()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var claimed = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);
        Assert.True(claimed.Result.IsSuccess);

        await using (var other = fixture.CreateContext())
        {
            var recipe = await other.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Unit.Id);
            recipe.Update("Edited", @"C:\edited.exe", ["x"], 5, false, Now.AddMinutes(2));
            await other.SaveChangesAsync(CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        var execution = await verify.VerificationExecutions.SingleAsync();
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Equal(scene.Unit.Name, execution.CommandName);
        Assert.Equal(scene.Unit.ExecutablePath, execution.ExecutablePath);
        Assert.Equal(scene.Unit.Arguments, execution.Arguments);
        Assert.Equal(scene.Unit.TimeoutSeconds, execution.TimeoutSeconds);
    }

    // ---- Concurrency --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_claims_in_one_workspace_create_exactly_one_running_execution_and_consume_one_number()
    {
        await using var seedContext = fixture.CreateContext();
        var scene = await SeedAsync(seedContext);
        var reader = new BarrierEvidenceReader(Fingerprint, 2);

        async Task<Result<ClaimVerificationExecutionCommandResult>> ClaimWith(Guid recipeId)
        {
            await using var context = fixture.CreateContext();
            return await new ClaimVerificationExecutionCommandHandler(context, reader, new FixedTimeProvider(Now)).HandleAsync(
                new ClaimVerificationExecutionCommand(scene.Project.Id, recipeId, scene.Checkpoint.Id), CancellationToken.None);
        }

        var results = await Task.WhenAll(ClaimWith(scene.Unit.Id), ClaimWith(scene.Lint.Id));

        Assert.Single(results, result => result.IsSuccess);
        var refused = Assert.Single(results, result => result.IsFailure);
        Assert.Equal("verification.already_running", refused.Errors[0].Code);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.VerificationExecutions.CountAsync(execution => execution.Status == VerificationExecutionStatus.Running));
        Assert.Equal(2, await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    // ---- The external observation and the transaction ----------------------------------------------------------------

    [Fact]
    public async Task Git_evidence_is_captured_once_before_the_transaction_begins_and_never_again_under_it()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(["capture", "begin"], outcome.Log);
    }

    [Fact]
    public async Task A_refused_observation_never_begins_a_transaction_and_creates_nothing()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null, capturedFingerprint: OtherFingerprint);

        await AssertRefusedAsync(scene, outcome.Result, "verification.checkpoint_not_current");
        Assert.Equal(["capture"], outcome.Log);
    }

    [Fact]
    public async Task Gates_that_fail_before_the_observation_do_no_external_work_and_open_no_transaction()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.VerificationCommands.ExecuteUpdateAsync(set => set.SetProperty(recipe => recipe.IsEnabled, false));

        var outcome = await ClaimAsync(dbContext, scene, Moment.BeforeBegin, drift: null);

        await AssertRefusedAsync(scene, outcome.Result, "verification.disabled");
        Assert.Empty(outcome.Log);
    }

    // ---- Atomic failure -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_save_leaves_no_execution_and_no_consumed_number()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(() => ClaimAsync(
            dbContext, scene, Moment.BeforeBegin, drift: null,
            configure: faulting => faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave));

        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_failed_commit_leaves_no_execution_and_no_consumed_number()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => ClaimAsync(
            dbContext, scene, Moment.BeforeBegin, drift: null,
            configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit));

        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_cancelled_save_leaves_no_execution_and_no_consumed_number()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ClaimAsync(
            dbContext, scene, Moment.BeforeBegin, drift: null,
            configure: faulting => faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationBeforeSave));

        await AssertNothingClaimedAsync(scene);
    }

    [Fact]
    public async Task A_failed_transaction_acquisition_creates_nothing()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => ClaimAsync(
            dbContext, scene, Moment.BeforeBegin, drift: null, configure: faulting => faulting.ThrowOnBeginTransaction = true));

        await AssertNothingClaimedAsync(scene);
    }

    // ---- Harness ------------------------------------------------------------------------------------------------------

    private sealed record ClaimOutcome(Result<ClaimVerificationExecutionCommandResult> Result, List<string> Log);

    private async Task<ClaimOutcome> ClaimAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        Moment moment,
        Func<DevalCopilotDbContext, Task>? drift,
        string? capturedFingerprint = null,
        Action<FaultInjectingDbContext>? configure = null)
    {
        var log = new List<string>();
        async Task ApplyDriftAsync()
        {
            if (drift is not null)
            {
                await using var other = fixture.CreateContext();
                await drift(other);
            }
        }

        var reader = new RecordingEvidenceReader(
            capturedFingerprint ?? Fingerprint,
            async () =>
            {
                log.Add("capture");
                if (moment == Moment.DuringCapture)
                {
                    await ApplyDriftAsync();
                }
            });
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                log.Add("begin");
                if (moment == Moment.BeforeBegin)
                {
                    await ApplyDriftAsync();
                }
            },
        };
        configure?.Invoke(faulting);

        var result = await new ClaimVerificationExecutionCommandHandler(faulting, reader, new FixedTimeProvider(Now)).HandleAsync(
            new ClaimVerificationExecutionCommand(scene.Project.Id, scene.Unit.Id, scene.Checkpoint.Id), CancellationToken.None);
        return new ClaimOutcome(result, log);
    }

    private async Task AssertRefusedAsync(
        Scene scene, Result<ClaimVerificationExecutionCommandResult> result, string code, int expectedExecutions = 0)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Errors[0].Code);
        await AssertNothingClaimedAsync(scene, expectedExecutions);
    }

    private async Task AssertNothingClaimedAsync(Scene scene, int expectedExecutions = 0)
    {
        await using var verify = fixture.CreateContext();
        Assert.Equal(expectedExecutions, await verify.VerificationExecutions.CountAsync());
        Assert.Equal(1, await verify.Projects.Where(project => project.Id == scene.Project.Id).Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    private sealed record Scene(
        Project Project,
        GitWorkspace Workspace,
        GitCheckpoint Checkpoint,
        VerificationCommand Unit,
        VerificationCommand Lint,
        GitWorkspace OtherWorkspace);

    private static async Task<Scene> SeedAsync(DevalCopilotDbContext dbContext, IReadOnlyCollection<string>? unitArguments = null)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = ReadyWorkspace(project, 1);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var unit = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", unitArguments ?? ["test"], 60, true, Now);
        var lint = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Lint", @"C:\dotnet.exe", ["format"], 60, true, Now);
        var other = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = ReadyWorkspace(other, 1);

        dbContext.Projects.AddRange(project, other);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.AddRange(unit, lint);
        dbContext.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), other.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Scene(project, workspace, checkpoint, unit, lint, otherWorkspace);
    }

    private static GitWorkspace ReadyWorkspace(Project project, int number)
    {
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, number, $@"C:\workspaces\{Guid.NewGuid():N}", $"branch-{number}", new string('a', 40), "main", Now);
        workspace.MarkReady();
        return workspace;
    }

    private sealed class RecordingEvidenceReader(string fingerprint, Func<Task> onCapture) : IGitWorkspaceEvidenceReader
    {
        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            await onCapture();
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null);
        }
    }

    /// <summary>Holds each capture until every participant has reached it, so concurrent claims interleave at the seam.</summary>
    private sealed class BarrierEvidenceReader(string fingerprint, int participants) : IGitWorkspaceEvidenceReader
    {
        private readonly Barrier barrier = new(participants);

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)), cancellationToken);
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
