using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The manual checkpoint review's commit seam over a real file-backed SQLite database. Every scenario keeps the
/// handler's own context alive and populated with the tracked entities it seeded, then lets a second connection commit
/// a change either while the external Git observation is running or immediately before the transaction begins. A tracked
/// entity is never a fresh authority read: the handler must see the second connection's commit and refuse or record from
/// fresh data, and a refusal must leave no review or evidence member.
/// </summary>
public sealed class RecordCheckpointReviewCommitSeamTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly string CheckpointFingerprint = new('b', 64);
    private static readonly string OtherFingerprint = new('c', 64);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    public enum Moment
    {
        DuringCapture,
        BeforeBegin,
    }

    // ---- Source drift: refused with no review or member ---------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture, ReviewDecision.Pending)]
    [InlineData(Moment.DuringCapture, ReviewDecision.Approved)]
    [InlineData(Moment.BeforeBegin, ReviewDecision.Pending)]
    [InlineData(Moment.BeforeBegin, ReviewDecision.Approved)]
    public async Task A_newer_checkpoint_committed_after_the_reads_is_never_reviewed_as_the_older_one(Moment moment, ReviewDecision decision)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, decision, async other =>
        {
            other.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.Workspace.Id, 2, Now.AddMinutes(1), new string('a', 40), OtherFingerprint, []));
            await other.SaveChangesAsync(CancellationToken.None);
        }, decision == ReviewDecision.Pending ? null : scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_newer_workspace_committed_after_the_reads_is_not_silently_targeted(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, async other =>
        {
            var newer = GitWorkspace.Prepare(
                Guid.NewGuid(), scene.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main", Now);
            newer.MarkReady();
            other.GitWorkspaces.Add(newer);
            other.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), scene.Project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), Now));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(outcome.Result, "reviews.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_workspace_that_stopped_being_ready_after_the_reads_refuses_the_review(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, other =>
            other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention)));

        await AssertRefusedAsync(outcome.Result, "reviews.workspace_not_ready");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_workspace_path_changed_after_the_reads_is_not_the_path_that_was_captured(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, other =>
            other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.WorkspacePath, @"C:\workspaces\moved")));

        await AssertRefusedAsync(outcome.Result, "reviews.checkpoint_not_current");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_lease_lost_after_the_reads_refuses_the_review(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, other =>
            other.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released)));

        await AssertRefusedAsync(outcome.Result, "reviews.workspace_not_ready");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_checkpoint_fingerprint_changed_after_the_reads_is_not_the_observed_source(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, other =>
            other.GitCheckpoints.Where(checkpoint => checkpoint.Id == scene.Checkpoint.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(checkpoint => checkpoint.FingerprintSha256, OtherFingerprint)));

        await AssertRefusedAsync(outcome.Result, "reviews.checkpoint_not_current");
    }

    // ---- Selected execution drift ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_that_changed_project_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ProjectId, scene.OtherProject.Id)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_that_changed_workspace_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.GitWorkspaceId, scene.OtherWorkspace.Id)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_that_changed_checkpoint_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.GitCheckpointId, scene.OtherCheckpoint.Id)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_fingerprint_changed_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Escalated, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.CheckpointFingerprintSha256, OtherFingerprint)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_deleted_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id).ExecuteDeleteAsync(), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_that_stopped_being_passed_after_the_reads_cannot_approve(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id).ExecuteUpdateAsync(set => set
                .SetProperty(execution => execution.Status, VerificationExecutionStatus.Failed)
                .SetProperty(execution => execution.ExitCode, 1)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.approval_requires_passed_verification");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task An_execution_that_is_running_again_after_the_reads_is_not_terminal_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Status, VerificationExecutionStatus.Running)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_terminal");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task Incoherent_terminal_evidence_is_refused_instead_of_failing_the_request(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ExitCode, 7)), scene.Passed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_terminal");
    }

    // ---- Fresh evidence at the commit boundary -------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_still_eligible_execution_is_recorded_from_its_fresh_state_not_the_tracked_one(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.ChangesRequested, other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.Passed.Id).ExecuteUpdateAsync(set => set
                .SetProperty(execution => execution.Status, VerificationExecutionStatus.Failed)
                .SetProperty(execution => execution.ExitCode, 1)), scene.Passed.Id);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        var member = Assert.Single(review.Evidence);
        Assert.Equal(scene.Passed.Id, member.VerificationExecutionId);
        Assert.Equal(VerificationExecutionStatus.Failed, member.VerificationExecutionStatus);
        Assert.Equal(1, member.VerificationExecutionExitCode);
    }

    // ---- Existing policy positive controls --------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task Pending_records_the_exact_current_checkpoint_without_any_member(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Pending, drift: null, actor: ReviewActorKind.FutureAgent);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(ReviewDecision.Pending, review.Decision);
        Assert.Equal(ReviewActorKind.FutureAgent, review.ActorKind);
        Assert.Equal(scene.Checkpoint.Id, review.GitCheckpointId);
        Assert.Equal(1, review.CheckpointNumber);
        Assert.Equal(CheckpointFingerprint, review.CheckpointFingerprintSha256);
        Assert.Equal(scene.Workspace.Id, review.GitWorkspaceId);
        Assert.Empty(review.Evidence);
        Assert.Empty(verify.CheckpointReviewEvidence);
    }

    [Fact]
    public async Task Approved_cites_exactly_the_selected_passed_execution_even_when_a_newer_one_failed()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, drift: null, executionId: scene.Passed.Id);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        var member = Assert.Single(review.Evidence);
        Assert.Equal(ReviewActorKind.Human, review.ActorKind);
        Assert.Equal(scene.Passed.Id, member.VerificationExecutionId);
        Assert.Equal(scene.Passed.ExecutionNumber, member.VerificationExecutionNumber);
        Assert.Equal(VerificationExecutionStatus.Passed, member.VerificationExecutionStatus);
        Assert.True(scene.Failed.ExecutionNumber > scene.Passed.ExecutionNumber);
    }

    [Fact]
    public async Task Approved_with_the_newer_failed_execution_is_still_refused()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, drift: null, executionId: scene.Failed.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.approval_requires_passed_verification");
    }

    [Theory]
    [InlineData(ReviewDecision.ChangesRequested)]
    [InlineData(ReviewDecision.Escalated)]
    public async Task Changes_requested_and_escalated_may_cite_any_terminal_execution_of_the_exact_checkpoint(ReviewDecision decision)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, decision, drift: null, executionId: scene.Failed.Id);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(decision, review.Decision);
        Assert.Equal(scene.Failed.Id, Assert.Single(review.Evidence).VerificationExecutionId);
    }

    [Fact]
    public async Task A_decision_with_a_foreign_execution_is_not_found_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.ChangesRequested, drift: null, executionId: scene.Foreign.Id);

        await AssertRefusedAsync(outcome.Result, "reviews.evidence_not_found");
    }

    // ---- The external observation and the transaction ------------------------------------------------------------

    [Fact]
    public async Task Git_evidence_is_captured_once_before_the_transaction_begins_and_never_again_under_it()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, drift: null, executionId: scene.Passed.Id);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(["capture", "begin"], outcome.Log);
    }

    [Fact]
    public async Task A_refused_observation_never_begins_a_transaction()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, drift: null, capturedFingerprint: OtherFingerprint);

        await AssertRefusedAsync(outcome.Result, "reviews.checkpoint_not_current");
        Assert.Equal(["capture"], outcome.Log);
    }

    [Fact]
    public async Task Source_gates_that_fail_before_the_observation_do_no_external_work_and_open_no_transaction()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.RepositoryMutationLeases.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, drift: null);

        await AssertRefusedAsync(outcome.Result, "reviews.workspace_not_ready");
        Assert.Empty(outcome.Log);
    }

    // ---- Atomic failure -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_save_leaves_no_review_and_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, drift: null, executionId: scene.Passed.Id,
            configure: faulting => faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave));

        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_failed_commit_leaves_no_review_and_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, drift: null, executionId: scene.Passed.Id,
            configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit));

        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_cancelled_save_leaves_no_review_and_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<OperationCanceledException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Escalated, drift: null, executionId: scene.Failed.Id,
            configure: faulting => faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationBeforeSave));

        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_failed_transaction_acquisition_records_nothing()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, drift: null,
            configure: faulting => faulting.ThrowOnBeginTransaction = true));

        await AssertNothingRecordedAsync();
    }

    // ---- Harness ------------------------------------------------------------------------------------------------------

    private sealed record SubmitOutcome(Result<RecordCheckpointReviewCommandResult> Result, List<string> Log);

    private async Task<SubmitOutcome> SubmitAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        Moment moment,
        ReviewDecision decision,
        Func<DevalCopilotDbContext, Task>? drift,
        Guid? executionId = null,
        ReviewActorKind actor = ReviewActorKind.Human,
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
            capturedFingerprint ?? CheckpointFingerprint,
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

        var result = await new RecordCheckpointReviewCommandHandler(faulting, reader, new FixedTimeProvider(Now)).HandleAsync(
            new RecordCheckpointReviewCommand(
                scene.Project.Id, scene.Checkpoint.Id, decision == ReviewDecision.Pending ? null : executionId, actor, decision),
            CancellationToken.None);
        return new SubmitOutcome(result, log);
    }

    private async Task AssertRefusedAsync(Result<RecordCheckpointReviewCommandResult> result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Errors[0].Code);
        await AssertNothingRecordedAsync();
    }

    private async Task AssertNothingRecordedAsync()
    {
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.CheckpointReviews.CountAsync());
        Assert.Equal(0, await verify.CheckpointReviewEvidence.CountAsync());
    }

    private sealed record Scene(
        Project Project,
        GitWorkspace Workspace,
        GitCheckpoint Checkpoint,
        VerificationExecution Passed,
        VerificationExecution Failed,
        Project OtherProject,
        GitWorkspace OtherWorkspace,
        GitCheckpoint OtherCheckpoint,
        VerificationExecution Foreign);

    /// <summary>One project whose current workspace is Ready with an active lease and one current checkpoint, an older Passed
    /// execution and a newer Failed one (two recipes, a third disabled recipe with no execution), plus a second project owning
    /// its own workspace, checkpoint and execution. The caller keeps the seeding context alive as the handler's own.</summary>
    private static async Task<Scene> SeedAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = ReadyWorkspace(project, 1);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), CheckpointFingerprint, []);
        var unit = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var lint = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 2, "Lint", @"C:\dotnet.exe", ["format"], 60, true, Now);
        var disabled = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 3, "Disabled", @"C:\dotnet.exe", ["x"], 60, false, Now);
        var passed = Terminal(project, workspace, checkpoint, unit, 1, VerificationExecutionStatus.Passed);
        var failed = Terminal(project, workspace, checkpoint, lint, 2, VerificationExecutionStatus.Failed);

        var otherProject = Project.Register(Guid.NewGuid(), "Other project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = ReadyWorkspace(otherProject, 1);
        var otherCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), otherWorkspace.Id, 1, Now, new string('a', 40), OtherFingerprint, []);
        var otherCommand = VerificationCommand.Configure(Guid.NewGuid(), otherProject.Id, 1, "Other", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var foreign = Terminal(otherProject, otherWorkspace, otherCheckpoint, otherCommand, 7, VerificationExecutionStatus.Passed);

        dbContext.Projects.AddRange(project, otherProject);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.AddRange(checkpoint, otherCheckpoint);
        dbContext.VerificationCommands.AddRange(unit, lint, disabled, otherCommand);
        dbContext.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), otherProject.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.VerificationExecutions.AddRange(passed, failed, foreign);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Scene(project, workspace, checkpoint, passed, failed, otherProject, otherWorkspace, otherCheckpoint, foreign);
    }

    private static GitWorkspace ReadyWorkspace(Project project, int number)
    {
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, number, $@"C:\workspaces\{Guid.NewGuid():N}", $"branch-{number}", new string('a', 40), "main", Now);
        workspace.MarkReady();
        return workspace;
    }

    private static VerificationExecution Terminal(
        Project project,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        VerificationCommand command,
        int number,
        VerificationExecutionStatus status)
    {
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, number, workspace, checkpoint, command, Now);
        execution.MarkDispatched(Now);
        execution.Complete(VerificationExecutionOutcome.Exited, status == VerificationExecutionStatus.Passed ? 0 : 1, checkpoint.FingerprintSha256, Now);
        return execution;
    }

    private sealed class RecordingEvidenceReader(string fingerprint, Func<Task> onCapture) : IGitWorkspaceEvidenceReader
    {
        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            await onCapture();
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
