using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The Human execution-set review (ADR-0030) over a real file-backed SQLite database. Every scenario keeps the handler's own
/// context alive and populated with the tracked entities it seeded, so a stale tracked row can never stand in for the fresh,
/// untracked authority read the handler performs under its write lock. A refusal leaves no review and no evidence member.
/// </summary>
public sealed class RecordCheckpointReviewCompleteSetTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
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

    // ---- Positive: the complete set ------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_enabled_recipes_are_approved_as_one_review_with_both_members_in_command_order()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.LintPassed.Id, scene.UnitPassed.Id]);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(ReviewActorKind.Human, review.ActorKind);
        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Equal(scene.Checkpoint.Id, review.GitCheckpointId);
        Assert.Equal(Fingerprint, review.CheckpointFingerprintSha256);
        Assert.Equal(
            new[] { (scene.Unit.Id, scene.UnitPassed.Id), (scene.Lint.Id, scene.LintPassed.Id) }.ToHashSet(),
            review.Evidence.Select(member => (member.VerificationCommandId, member.VerificationExecutionId)).ToHashSet());
        Assert.All(review.Evidence, member => Assert.Equal(VerificationExecutionStatus.Passed, member.VerificationExecutionStatus));

        // The stored rows have no order and EF inserts them by key, so the canonical CommandNumber order is the read model's: the
        // history lists the members as Unit then Lint however the caller listed them.
        var history = await new GetProjectCheckpointReviewsQueryHandler(verify, new RecordingEvidenceReader(Fingerprint, () => Task.CompletedTask))
            .HandleAsync(new GetProjectCheckpointReviewsQuery(scene.Project.Id), CancellationToken.None);
        Assert.Equal(
            [scene.Unit.Id, scene.Lint.Id],
            Assert.Single(history.Value).Evidence.Select(member => member.VerificationCommandId));
    }

    [Fact]
    public async Task The_history_lists_members_in_command_order_for_every_submitted_order()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]);
        await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.ChangesRequested, [scene.LintFailed.Id, scene.UnitPassed.Id]);

        await using var verify = fixture.CreateContext();
        var history = await new GetProjectCheckpointReviewsQueryHandler(verify, new RecordingEvidenceReader(Fingerprint, () => Task.CompletedTask))
            .HandleAsync(new GetProjectCheckpointReviewsQuery(scene.Project.Id), CancellationToken.None);

        Assert.Equal(2, history.Value.Count);
        Assert.All(history.Value, review => Assert.Equal([scene.Unit.Id, scene.Lint.Id], review.Evidence.Select(member => member.VerificationCommandId)));
    }

    [Fact]
    public async Task More_than_twenty_executions_never_hide_the_complete_set()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        for (var number = 10; number < 40; number++)
        {
            await AddExecutionAsync(dbContext, scene, number % 2 == 0 ? scene.Unit : scene.Lint, number, VerificationExecutionStatus.Failed);
        }

        var latestUnit = await AddExecutionAsync(dbContext, scene, scene.Unit, 50, VerificationExecutionStatus.Passed);
        var latestLint = await AddExecutionAsync(dbContext, scene, scene.Lint, 51, VerificationExecutionStatus.Passed);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [latestUnit.Id, latestLint.Id]);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(
            new[] { latestUnit.Id, latestLint.Id }.ToHashSet(),
            review.Evidence.Select(member => member.VerificationExecutionId).ToHashSet());
        Assert.True(await verify.VerificationExecutions.CountAsync(execution => execution.ProjectId == scene.Project.Id) > 20);
    }

    [Fact]
    public async Task A_disabled_recipe_is_not_part_of_the_complete_set()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.CheckpointReviewEvidence.CountAsync());
    }

    [Theory]
    [InlineData(ReviewDecision.ChangesRequested)]
    [InlineData(ReviewDecision.Escalated)]
    public async Task Changes_requested_and_escalated_may_cite_a_coherent_terminal_set(ReviewDecision decision)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, decision, [scene.LintFailed.Id, scene.UnitPassed.Id]);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(decision, review.Decision);
        Assert.Equal(
            new[] { VerificationExecutionStatus.Passed, VerificationExecutionStatus.Failed }.ToHashSet(),
            review.Evidence.Select(member => member.VerificationExecutionStatus).ToHashSet());
    }

    [Fact]
    public async Task Changes_requested_may_cite_an_older_execution_because_only_approval_requires_the_latest()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.ChangesRequested, [scene.UnitPassed.Id, scene.LintFailed.Id]);

        Assert.True(outcome.Result.IsSuccess);
    }

    [Fact]
    public async Task An_empty_set_is_a_pending_review_with_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, []);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(ReviewDecision.Pending, review.Decision);
        Assert.Empty(review.Evidence);
    }

    // ---- Compatibility: the legacy scalar form -------------------------------------------------------------------------

    [Fact]
    public async Task The_legacy_scalar_approval_stays_a_single_member_fact_even_with_two_enabled_recipes()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, ids: null, scalar: scene.UnitPassed.Id);

        Assert.True(outcome.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var review = Assert.Single(verify.CheckpointReviews.Include(candidate => candidate.Evidence));
        Assert.Equal(scene.UnitPassed.Id, Assert.Single(review.Evidence).VerificationExecutionId);
    }

    [Fact]
    public async Task The_legacy_scalar_form_and_a_one_recipe_set_record_the_same_single_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext, enableLint: false);

        var scalar = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, ids: null, scalar: scene.UnitPassed.Id);
        var set = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id]);

        Assert.True(scalar.Result.IsSuccess);
        Assert.True(set.Result.IsSuccess);
        await using var verify = fixture.CreateContext();
        var reviews = await verify.CheckpointReviews.Include(candidate => candidate.Evidence).ToListAsync();
        Assert.Equal(2, reviews.Count);
        Assert.All(reviews, review =>
        {
            var member = Assert.Single(review.Evidence);
            Assert.Equal((scene.Unit.Id, scene.UnitPassed.Id), (member.VerificationCommandId, member.VerificationExecutionId));
        });
    }

    // ---- Request shape: refused before any observation ------------------------------------------------------------------

    [Fact]
    public async Task Two_non_null_evidence_forms_are_refused_never_merged_and_never_observe_git()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.LintPassed.Id], scalar: scene.UnitPassed.Id);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.AmbiguousCode);
        Assert.Empty(outcome.Log);
    }

    [Fact]
    public async Task An_empty_set_with_a_scalar_is_still_two_forms()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, [], scalar: scene.UnitPassed.Id);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.AmbiguousCode);
    }

    [Fact]
    public async Task More_than_thirty_two_identifiers_are_refused_never_truncated()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var many = Enumerable.Range(0, 33).Select(_ => Guid.NewGuid()).ToArray();

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, many);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.InvalidCode);
        Assert.Empty(outcome.Log);
    }

    [Fact]
    public async Task Exactly_thirty_two_identifiers_pass_the_shape_gate_and_fail_on_binding_instead()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var many = Enumerable.Range(0, 32).Select(_ => Guid.NewGuid()).ToArray();

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, many);

        await AssertRefusedAsync(outcome, "reviews.evidence_not_found");
    }

    [Fact]
    public async Task A_duplicate_identifier_is_refused_never_silently_deduplicated()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id, scene.UnitPassed.Id]);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.InvalidCode);
        Assert.Empty(outcome.Log);
    }

    [Fact]
    public async Task An_empty_identifier_is_refused()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, Guid.Empty]);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.InvalidCode);
    }

    [Theory]
    [InlineData(ReviewDecision.Approved)]
    [InlineData(ReviewDecision.ChangesRequested)]
    [InlineData(ReviewDecision.Escalated)]
    public async Task A_decided_review_with_an_empty_set_is_refused(ReviewDecision decision)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, decision, []);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.InvalidCode);
    }

    [Fact]
    public async Task A_pending_review_with_members_is_refused()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Pending, [scene.UnitPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.pending_cannot_include_evidence");
        Assert.Empty(outcome.Log);
    }

    [Theory]
    [InlineData(ReviewDecision.Approved)]
    [InlineData(ReviewDecision.Pending)]
    public async Task The_set_form_is_only_for_human_reviews(ReviewDecision decision)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, decision, decision == ReviewDecision.Pending ? [] : [scene.UnitPassed.Id, scene.LintPassed.Id],
            actor: ReviewActorKind.FutureAgent);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.SetRequiresHumanCode);
        Assert.Empty(outcome.Log);
    }

    // ---- Binding and incomplete sets ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_foreign_execution_is_not_found_and_never_persisted()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.Foreign.Id]);

        await AssertRefusedAsync(outcome, "reviews.evidence_not_found");
    }

    [Fact]
    public async Task An_execution_of_another_checkpoint_of_the_same_project_is_not_found()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id, scene.OldCheckpointPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.evidence_not_found");
    }

    [Fact]
    public async Task A_subset_of_the_enabled_recipes_cannot_be_approved()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Fact]
    public async Task A_set_with_a_member_beyond_the_enabled_recipes_cannot_be_approved()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id, scene.DisabledPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Fact]
    public async Task Two_executions_of_one_recipe_are_never_two_members()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var newerUnit = await AddExecutionAsync(dbContext, scene, scene.Unit, 30, VerificationExecutionStatus.Passed);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, newerUnit.Id]);

        await AssertRefusedAsync(outcome, CheckpointReviewEvidenceSelection.InvalidCode);
    }

    [Fact]
    public async Task An_older_passed_execution_never_stands_in_for_a_newer_failed_latest()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await AddExecutionAsync(dbContext, scene, scene.Lint, 30, VerificationExecutionStatus.Failed);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Fact]
    public async Task A_recipe_without_any_execution_for_the_checkpoint_blocks_the_approval()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var extra = VerificationCommand.Configure(Guid.NewGuid(), scene.Project.Id, 4, "Format", @"C:\dotnet.exe", ["format"], 60, true, Now);
        dbContext.VerificationCommands.Add(extra);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Fact]
    public async Task A_non_passed_member_is_refused_for_approval_with_the_existing_code()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintFailed.Id]);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_passed_verification");
    }

    [Fact]
    public async Task A_running_member_is_not_terminal_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var running = VerificationExecution.Claim(Guid.NewGuid(), scene.Project.Id, 60, scene.Workspace, scene.Checkpoint, scene.Lint, Now);
        dbContext.VerificationExecutions.Add(running);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.ChangesRequested, [scene.UnitPassed.Id, running.Id]);

        await AssertRefusedAsync(outcome, "reviews.evidence_not_terminal");
    }

    [Fact]
    public async Task More_than_thirty_two_enabled_recipes_have_no_admissible_complete_set()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var executions = new List<Guid> { scene.UnitPassed.Id, scene.LintPassed.Id };
        for (var number = 10; number < 41; number++)
        {
            var command = VerificationCommand.Configure(Guid.NewGuid(), scene.Project.Id, number, $"Recipe {number}", @"C:\dotnet.exe", ["x"], 60, true, Now);
            dbContext.VerificationCommands.Add(command);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            executions.Add((await AddExecutionAsync(dbContext, scene, command, 100 + number, VerificationExecutionStatus.Passed)).Id);
        }

        Assert.Equal(33, executions.Count);
        var thirtyTwo = executions.Take(32).ToArray();
        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, thirtyTwo);

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    // ---- Stale authority at the commit boundary --------------------------------------------------------------------------

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_newer_failed_execution_after_the_reads_refuses_the_approval(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: async other =>
        {
            other.VerificationExecutions.Add(Terminal(scene.Project, scene.Workspace, scene.Checkpoint, scene.Lint, 90, VerificationExecutionStatus.Failed));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_recipe_enabled_after_the_reads_makes_the_set_incomplete(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationCommands.Where(command => command.Id == scene.Disabled.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(command => command.IsEnabled, true)));

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_recipe_disabled_after_the_reads_changes_the_membership_the_caller_submitted(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationCommands.Where(command => command.Id == scene.Lint.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(command => command.IsEnabled, false)));

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_recipe_reconfigured_after_the_reads_no_longer_matches_its_execution_snapshot(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationCommands.Where(command => command.Id == scene.Unit.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(command => command.TimeoutSeconds, 61)));

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_member_that_changed_completion_fingerprint_after_the_reads_is_not_a_clean_pass(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.LintPassed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.CompletionFingerprintSha256, OtherFingerprint)));

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_member_that_changed_workspace_path_after_the_reads_is_not_a_clean_pass(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.UnitPassed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.WorkspacePath, @"C:\somewhere\else")));

        await AssertRefusedAsync(outcome, "reviews.approval_requires_complete_verification_set");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_member_that_changed_project_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.LintPassed.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ProjectId, scene.Foreign.ProjectId)));

        await AssertRefusedAsync(outcome, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_member_deleted_after_the_reads_is_not_evidence(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.VerificationExecutions.Where(execution => execution.Id == scene.LintPassed.Id).ExecuteDeleteAsync());

        await AssertRefusedAsync(outcome, "reviews.evidence_not_found");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_workspace_reserved_after_the_reads_refuses_even_a_complete_set(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: other =>
            other.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.Committing)));

        await AssertRefusedAsync(outcome, "reviews.workspace_not_ready");
    }

    [Theory]
    [InlineData(Moment.DuringCapture)]
    [InlineData(Moment.BeforeBegin)]
    public async Task A_newer_checkpoint_after_the_reads_refuses_the_set(Moment moment)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, moment, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id], drift: async other =>
        {
            other.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.Workspace.Id, 3, Now.AddMinutes(1), new string('a', 40), OtherFingerprint, []));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        await AssertRefusedAsync(outcome, "reviews.checkpoint_not_current");
    }

    [Fact]
    public async Task A_drift_free_complete_set_is_recorded_from_fresh_state_under_one_capture_and_one_begin()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var outcome = await SubmitAsync(dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(["capture", "begin"], outcome.Log);
    }

    // ---- Atomic failure ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_save_of_a_multi_member_review_leaves_no_review_and_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id],
            configure: faulting => faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave));

        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_failed_commit_of_a_multi_member_review_leaves_no_review_and_no_member()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        await Assert.ThrowsAsync<FaultInjectingDbContext.SimulatedDbException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id],
            configure: faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit));

        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_second_member_that_the_database_refuses_rolls_the_whole_review_back()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbContext.Database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only trigger assembled from a constant and a test-owned identifier.
            command.CommandText =
                "CREATE TRIGGER trg_test_second_member BEFORE INSERT ON checkpoint_review_evidence "
                + $"WHEN NEW.\"VerificationExecutionId\" = '{scene.LintPassed.Id.ToString().ToUpperInvariant()}' "
                + "BEGIN SELECT RAISE(ABORT, 'test_second_member_refused'); END;";
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitAsync(
            dbContext, scene, Moment.BeforeBegin, ReviewDecision.Approved, [scene.UnitPassed.Id, scene.LintPassed.Id]));

        await AssertNothingRecordedAsync();
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.CheckpointReviewEvidence.CountAsync());
    }

    // ---- Harness ------------------------------------------------------------------------------------------------------

    private sealed record SubmitOutcome(Result<RecordCheckpointReviewCommandResult> Result, List<string> Log);

    private async Task<SubmitOutcome> SubmitAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        Moment moment,
        ReviewDecision decision,
        IReadOnlyList<Guid>? ids,
        Guid? scalar = null,
        Func<DevalCopilotDbContext, Task>? drift = null,
        ReviewActorKind actor = ReviewActorKind.Human,
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
            Fingerprint,
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
            new RecordCheckpointReviewCommand(scene.Project.Id, scene.Checkpoint.Id, scalar, actor, decision, ids),
            CancellationToken.None);
        return new SubmitOutcome(result, log);
    }

    private async Task AssertRefusedAsync(SubmitOutcome outcome, string code)
    {
        Assert.True(outcome.Result.IsFailure);
        Assert.Equal(code, outcome.Result.Errors[0].Code);
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
        VerificationCommand Unit,
        VerificationCommand Lint,
        VerificationCommand Disabled,
        VerificationExecution UnitPassed,
        VerificationExecution LintFailed,
        VerificationExecution LintPassed,
        VerificationExecution DisabledPassed,
        VerificationExecution OldCheckpointPassed,
        VerificationExecution Foreign);

    /// <summary>One project with two enabled recipes (Unit, Lint) and one disabled recipe, a Ready workspace with an active lease and
    /// one current checkpoint. Unit has one Passed execution; Lint has an older Failed and a newer Passed one; the disabled recipe has
    /// a Passed execution; an older checkpoint of the same project has a Passed execution; a second project owns a Passed execution.
    /// The caller keeps the seeding context alive as the handler's own, populated with every tracked entity.</summary>
    private static async Task<Scene> SeedAsync(DevalCopilotDbContext dbContext, bool enableLint = true)
    {
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = ReadyWorkspace(project, 1);
        var oldCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), OtherFingerprint, []);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('a', 40), Fingerprint, []);
        var unit = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var lint = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 2, "Lint", @"C:\dotnet.exe", ["format"], 60, enableLint, Now);
        var disabled = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 3, "Disabled", @"C:\dotnet.exe", ["x"], 60, false, Now);
        var unitPassed = Terminal(project, workspace, checkpoint, unit, 1, VerificationExecutionStatus.Passed);
        var lintFailed = Terminal(project, workspace, checkpoint, lint, 2, VerificationExecutionStatus.Failed);
        var lintPassed = Terminal(project, workspace, checkpoint, lint, 3, VerificationExecutionStatus.Passed);
        var disabledPassed = Terminal(project, workspace, checkpoint, disabled, 4, VerificationExecutionStatus.Passed);
        var oldPassed = Terminal(project, workspace, oldCheckpoint, unit, 5, VerificationExecutionStatus.Passed);

        var otherProject = Project.Register(Guid.NewGuid(), "Other project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = ReadyWorkspace(otherProject, 1);
        var otherCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), otherWorkspace.Id, 1, Now, new string('a', 40), OtherFingerprint, []);
        var otherCommand = VerificationCommand.Configure(Guid.NewGuid(), otherProject.Id, 1, "Other", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var foreign = Terminal(otherProject, otherWorkspace, otherCheckpoint, otherCommand, 7, VerificationExecutionStatus.Passed);

        dbContext.Projects.AddRange(project, otherProject);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.AddRange(oldCheckpoint, checkpoint, otherCheckpoint);
        dbContext.VerificationCommands.AddRange(unit, lint, disabled, otherCommand);
        dbContext.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), otherProject.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.VerificationExecutions.AddRange(unitPassed, lintFailed, lintPassed, disabledPassed, oldPassed, foreign);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Scene(project, workspace, checkpoint, unit, lint, disabled, unitPassed, lintFailed, lintPassed, disabledPassed, oldPassed, foreign);
    }

    private static async Task<VerificationExecution> AddExecutionAsync(
        DevalCopilotDbContext dbContext, Scene scene, VerificationCommand command, int number, VerificationExecutionStatus status)
    {
        var execution = Terminal(scene.Project, scene.Workspace, scene.Checkpoint, command, number, status);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return execution;
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
