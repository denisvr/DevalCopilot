using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The claim boundaries of the optional second challenge round: a Resolver's first
/// revision may be reviewed once, a challenged review of it may be resolved once, the resulting
/// depth-two revision is the end of the lineage, and a first revision is implementable only with its
/// exact evidence and never after a Challenged review. Each refusal is proved to do no external work
/// and leave no attempt or sealed manifest behind.</summary>
public sealed class SecondChallengeRoundClaimTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private CreateClaudeCriticalReviewAttemptCommandHandler ReviewHandler(
        DevalCopilotDbContext dbContext, CountingEvidenceReader evidence, RecordingArtifactStore store) =>
        new(dbContext, evidence, store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options));

    private CreateChallengeResolutionAttemptCommandHandler ResolutionHandler(
        DevalCopilotDbContext dbContext, CountingEvidenceReader evidence, RecordingArtifactStore store) =>
        new(dbContext, evidence, store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options));

    private static CreateImplementationAttemptCommandHandler ImplementationHandler(
        DevalCopilotDbContext dbContext, CountingEvidenceReader evidence, RecordingArtifactStore store) =>
        new(dbContext, evidence, store, new FixedTimeProvider(Now));

    private static Task<int> AgentAttemptCountAsync(DevalCopilotDbContext dbContext, Guid runId) =>
        dbContext.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Agent);

    private async Task CommitConcurrentlyAsync(Scene scene, int firstAttemptNumber, Action<PlanningLineageSeeder> arrange)
    {
        await using var other = _fixture.CreateContext();
        arrange(SeederFor(other, scene, firstAttemptNumber));
        await other.SaveChangesAsync(CancellationToken.None);
    }

    // ---- Critical-review claim ---------------------------------------------------------------

    [Fact]
    public async Task A_first_revision_can_be_claimed_for_review_with_the_revision_as_its_sole_input()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var input = Assert.Single(dbContext.AttemptInputMessages, candidate => candidate.AttemptId == result.Value.AttemptId);
        Assert.Equal(0, input.Sequence);
        Assert.Equal(first.RevisedProposal.Id, input.CollaborationMessageId);
    }

    [Fact]
    public async Task The_original_planner_root_can_still_be_claimed_for_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var (_, root) = SeederFor(dbContext, scene).AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, root.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_second_revision_is_never_reviewable_and_is_refused_before_any_external_work()
    {
        await using var dbContext = _fixture.CreateContext();
        // The Claude runtime is deliberately not observed: the lineage refusal must come first, so
        // no provider probe, Git capture, or manifest sealing is ever reached.
        var scene = await SeedSceneAsync(dbContext, claudeObserved: false);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ReviewHandler(dbContext, evidence, store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, second.RevisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        Assert.Equal(before, await AgentAttemptCountAsync(dbContext, scene.Run.Id));
    }

    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    public async Task A_first_revision_that_already_has_a_successful_review_is_not_reviewed_again(AgentOutcome outcome)
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        seeder.AddReview(first.RevisedProposal, outcome);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var store = new RecordingArtifactStore();

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.already_reviewed", Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
        Assert.Equal(before, await AgentAttemptCountAsync(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task A_revision_with_incomplete_lineage_evidence_is_refused_without_sealing()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root, challengeCount: 2);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.AttemptInputMessages
            .Where(input => input.AttemptId == first.Attempt.Id && input.Sequence == 2)
            .ExecuteDeleteAsync();
        var store = new RecordingArtifactStore();

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.proposal_lineage_not_valid", Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
    }

    [Fact]
    public async Task A_competing_review_that_lands_while_the_manifest_is_sealed_is_refused_and_the_manifest_removed()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var next = seeder.NextAttemptNumber + 20;
        var store = new RecordingArtifactStore(onSeal: () => CommitConcurrentlyAsync(
            scene, next, competing => competing.AddReview(first.RevisedProposal, AgentOutcome.Challenged)));

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.already_reviewed", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await using var verification = _fixture.CreateContext();
        Assert.Equal(before + 1, await AgentAttemptCountAsync(verification, scene.Run.Id));
    }

    [Fact]
    public async Task An_attempt_that_starts_running_while_the_manifest_is_sealed_refuses_the_review_claim()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var (_, root) = SeederFor(dbContext, scene).AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var store = new RecordingArtifactStore(onSeal: async () =>
        {
            await using var other = _fixture.CreateContext();
            other.Attempts.Add(Attempt.ClaimAgent(
                Guid.NewGuid(), scene.Run.Id, 40, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, 40));
            await other.SaveChangesAsync(CancellationToken.None);
        });

        var result = await ReviewHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, root.Id), CancellationToken.None);

        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
    }

    // ---- Challenge-resolution claim ------------------------------------------------------------

    [Fact]
    public async Task A_challenged_review_of_a_first_revision_can_be_claimed_for_the_second_resolution()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged, challengeCount: 2);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ResolutionHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(scene.Run.Id, secondReview.Attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var inputs = dbContext.AttemptInputMessages
            .Where(input => input.AttemptId == result.Value.AttemptId)
            .OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId)
            .ToList();
        Assert.Equal(
            new[] { first.RevisedProposal.Id }.Concat(secondReview.Outputs.Select(challenge => challenge.Id)), inputs);
    }

    [Fact]
    public async Task A_second_resolution_is_not_claimed_twice_for_the_same_challenged_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (secondReview, _) = seeder.AddChallengedRound(first.RevisedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var store = new RecordingArtifactStore();

        var result = await ResolutionHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(scene.Run.Id, secondReview.Attempt.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.already_resolved", Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
    }

    [Fact]
    public async Task A_challenged_review_of_a_depth_two_proposal_can_never_be_resolved()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal);
        var impossibleReview = seeder.AddReview(second.RevisedProposal, AgentOutcome.Challenged);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var store = new RecordingArtifactStore();

        var result = await ResolutionHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(scene.Run.Id, impossibleReview.Attempt.Id), CancellationToken.None);

        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
    }

    [Fact]
    public async Task A_competing_resolution_that_lands_while_the_manifest_is_sealed_is_refused_inside_the_claim_transaction()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var next = seeder.NextAttemptNumber + 20;
        var store = new RecordingArtifactStore(onSeal: () => CommitConcurrentlyAsync(
            scene, next, competing => competing.AddResolution(first.RevisedProposal, secondReview.Outputs)));

        var result = await ResolutionHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(scene.Run.Id, secondReview.Attempt.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.already_resolved", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await using var verification = _fixture.CreateContext();
        Assert.Equal(before + 1, await AgentAttemptCountAsync(verification, scene.Run.Id));
    }

    // ---- Implementation claim ---------------------------------------------------------------------

    [Fact]
    public async Task A_first_revision_without_a_second_review_is_implementable_with_its_decision_evidence_only()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root, challengeCount: 2);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var store = new RecordingArtifactStore();

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(await InputsAsync(dbContext, result.Value.AttemptId), new[] { first.RevisedProposal.Id }.Concat(first.Decisions.Select(d => d.Id)));
        Assert.DoesNotContain("acceptedSecondReview", store.ReadManifest(scene.Run.Id, result.Value.AttemptId));
    }

    [Fact]
    public async Task A_first_revision_with_an_accepted_second_review_is_implementable_bound_to_that_exact_acceptance()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var acceptedReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Accepted);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var store = new RecordingArtifactStore();

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { first.RevisedProposal.Id }.Concat(first.Decisions.Select(d => d.Id)).Append(acceptedReview.Outputs[0].Id),
            await InputsAsync(dbContext, result.Value.AttemptId));
        var manifest = store.ReadManifest(scene.Run.Id, result.Value.AttemptId);
        Assert.Contains("acceptedSecondReview", manifest);
        Assert.Contains("Accepted as proposed.", manifest);
    }

    [Theory]
    [InlineData(SecondResolutionState.NotRequested)]
    [InlineData(SecondResolutionState.Failed)]
    [InlineData(SecondResolutionState.Succeeded)]
    public async Task A_first_revision_challenged_by_its_second_review_is_never_implementable(SecondResolutionState state)
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged);
        switch (state)
        {
            case SecondResolutionState.Failed:
                var failedNumber = seeder.NextAttemptNumber;
                var failed = Attempt.ClaimAgentChallengeResolution(
                    Guid.NewGuid(), scene.Run.Id, failedNumber, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
                    TimeSpan.FromMinutes(10), 262144, 524288, Now, failedNumber);
                failed.MarkAgentDispatched(Now);
                failed.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
                dbContext.Attempts.Add(failed);
                break;
            case SecondResolutionState.Succeeded:
                seeder.AddResolution(first.RevisedProposal, secondReview.Outputs);
                break;
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ImplementationHandler(dbContext, evidence, store)
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.plan_challenged", Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        Assert.Equal(before, await AgentAttemptCountAsync(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task A_depth_two_revision_is_never_implementable_even_after_its_escalation()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal);
        dbContext.CollaborationMessages.Add(PlanningEscalation.Record(
            scene.Run.Id, root.Id, first.RevisedProposal.Id, second.RevisedProposal.Id, [second.Decisions[0].Id], Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ImplementationHandler(dbContext, evidence, store)
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, second.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        Assert.Equal(before, await AgentAttemptCountAsync(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task An_accepted_second_review_whose_acceptance_does_not_reply_to_the_revision_is_refused()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var acceptedReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Accepted);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.CollaborationMessages
            .Where(message => message.Id == acceptedReview.Outputs[0].Id)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, root.Id));

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.acceptance_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_revised_plan_with_incomplete_lineage_evidence_is_refused()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root, challengeCount: 2);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.CollaborationMessages.Where(message => message.Id == first.Decisions[1].Id).ExecuteDeleteAsync();

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.decisions_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task The_original_accepted_planner_proposal_path_is_unchanged()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var review = seeder.AddReview(root, AgentOutcome.Accepted);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), new RecordingArtifactStore())
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, root.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { root.Id, review.Outputs[0].Id }, await InputsAsync(dbContext, result.Value.AttemptId));
    }

    [Fact]
    public async Task A_challenged_review_that_lands_while_the_manifest_is_sealed_refuses_the_implementation_claim()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var before = await AgentAttemptCountAsync(dbContext, scene.Run.Id);
        var next = seeder.NextAttemptNumber + 20;
        var store = new RecordingArtifactStore(onSeal: () => CommitConcurrentlyAsync(
            scene, next, competing => competing.AddReview(first.RevisedProposal, AgentOutcome.Challenged)));

        var result = await ImplementationHandler(dbContext, new CountingEvidenceReader(), store)
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.plan_challenged", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await using var verification = _fixture.CreateContext();
        Assert.Equal(before + 1, await AgentAttemptCountAsync(verification, scene.Run.Id));
        Assert.Empty(verification.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    public enum SecondResolutionState
    {
        NotRequested,
        Failed,
        Succeeded,
    }

    private static async Task<List<Guid>> InputsAsync(DevalCopilotDbContext dbContext, Guid attemptId) =>
        await dbContext.AttemptInputMessages
            .Where(input => input.AttemptId == attemptId)
            .OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId)
            .ToListAsync();
}
