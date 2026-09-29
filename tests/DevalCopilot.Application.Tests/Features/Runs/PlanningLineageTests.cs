using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class PlanningLineageTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static async Task<PlanningLineage.Evaluation> EvaluateAsync(
        DevalCopilotDbContext dbContext, Scene scene, Guid messageId, Guid? checkpointId = null)
    {
        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, scene.Run.Id, CancellationToken.None);
        return PlanningLineage.Evaluate(
            snapshot, scene.Run.Id, scene.Workspace.Id, checkpointId ?? scene.Checkpoint.Id, Fingerprint, messageId);
    }

    [Fact]
    public async Task A_planner_root_is_depth_zero_and_each_revision_adds_one_level()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root, challengeCount: 2);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal, challengeCount: 3);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var rootResult = await EvaluateAsync(dbContext, scene, root.Id);
        var firstResult = await EvaluateAsync(dbContext, scene, first.RevisedProposal.Id);
        var secondResult = await EvaluateAsync(dbContext, scene, second.RevisedProposal.Id);

        Assert.Equal(0, rootResult.Node!.Depth);
        Assert.Equal(1, firstResult.Node!.Depth);
        Assert.Equal(2, secondResult.Node!.Depth);
        Assert.Equal(root.Id, secondResult.Node.Root.Proposal.Id);
        Assert.Equal(first.RevisedProposal.Id, secondResult.Node.Parent!.Proposal.Id);
        Assert.Equal(3, secondResult.Node.Decisions.Count);
    }

    [Fact]
    public async Task A_revision_of_a_depth_two_proposal_is_not_a_valid_lineage_level()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal);
        var (_, third) = seeder.AddChallengedRound(second.RevisedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await EvaluateAsync(dbContext, scene, third.RevisedProposal.Id);

        Assert.Null(result.Node);
        Assert.Equal(PlanningLineage.FailureKind.LineageInvalid, result.Failure);
    }

    [Fact]
    public async Task A_message_id_from_another_run_or_an_unknown_id_is_not_found()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var otherScene = await SeedSceneAsync(dbContext, seedCapabilities: false);
        var (_, foreignRoot) = SeederFor(dbContext, otherScene).AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PlanningLineage.FailureKind.NotFound, (await EvaluateAsync(dbContext, scene, foreignRoot.Id)).Failure);
        Assert.Equal(PlanningLineage.FailureKind.NotFound, (await EvaluateAsync(dbContext, scene, Guid.NewGuid())).Failure);
    }

    [Fact]
    public async Task A_lineage_bound_to_a_different_checkpoint_is_stale_at_every_level()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var stale = Guid.NewGuid();
        Assert.Equal(PlanningLineage.FailureKind.CheckpointStale, (await EvaluateAsync(dbContext, scene, root.Id, stale)).Failure);
        Assert.Equal(
            PlanningLineage.FailureKind.CheckpointStale, (await EvaluateAsync(dbContext, scene, first.RevisedProposal.Id, stale)).Failure);
    }

    public enum Corruption
    {
        IncompleteChallengeSet,
        MissingDecision,
        DecisionRepliesToWrongChallenge,
        SecondChallengedReviewOfTheParent,
        SimulatedRevisedProposal,
        SelfReferencingResolverInput,
        RevisedProposalRepliesToWrongParent,
        ChallengeRepliesToWrongProposal,
        ReviewNoLongerChallenged,
    }

    [Theory]
    [InlineData(Corruption.IncompleteChallengeSet)]
    [InlineData(Corruption.MissingDecision)]
    [InlineData(Corruption.DecisionRepliesToWrongChallenge)]
    [InlineData(Corruption.SecondChallengedReviewOfTheParent)]
    [InlineData(Corruption.SimulatedRevisedProposal)]
    [InlineData(Corruption.SelfReferencingResolverInput)]
    [InlineData(Corruption.RevisedProposalRepliesToWrongParent)]
    [InlineData(Corruption.ChallengeRepliesToWrongProposal)]
    [InlineData(Corruption.ReviewNoLongerChallenged)]
    public async Task A_corrupt_duplicated_or_cyclic_revision_fails_closed_without_mutation(Corruption corruption)
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (review, resolution) = seeder.AddChallengedRound(root, challengeCount: 2);
        if (corruption == Corruption.SecondChallengedReviewOfTheParent)
        {
            seeder.AddReview(root, AgentOutcome.Challenged, 2);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        switch (corruption)
        {
            case Corruption.IncompleteChallengeSet:
                await dbContext.AttemptInputMessages
                    .Where(input => input.AttemptId == resolution.Attempt.Id && input.Sequence == 2)
                    .ExecuteDeleteAsync();
                break;
            case Corruption.MissingDecision:
                await dbContext.CollaborationMessages.Where(message => message.Id == resolution.Decisions[1].Id).ExecuteDeleteAsync();
                break;
            case Corruption.DecisionRepliesToWrongChallenge:
                await dbContext.CollaborationMessages
                    .Where(message => message.Id == resolution.Decisions[0].Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, review.Outputs[1].Id));
                break;
            case Corruption.SimulatedRevisedProposal:
                await dbContext.CollaborationMessages
                    .Where(message => message.Id == resolution.RevisedProposal.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.Provenance, CollaborationMessageProvenance.Simulated));
                break;
            case Corruption.SelfReferencingResolverInput:
                await dbContext.AttemptInputMessages
                    .Where(input => input.AttemptId == resolution.Attempt.Id && input.Sequence == 0)
                    .ExecuteUpdateAsync(set => set.SetProperty(input => input.CollaborationMessageId, resolution.RevisedProposal.Id));
                break;
            case Corruption.RevisedProposalRepliesToWrongParent:
                await dbContext.CollaborationMessages
                    .Where(message => message.Id == resolution.RevisedProposal.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, review.Outputs[0].Id));
                break;
            case Corruption.ChallengeRepliesToWrongProposal:
                await dbContext.CollaborationMessages
                    .Where(message => message.Id == review.Outputs[0].Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, resolution.RevisedProposal.Id));
                break;
            case Corruption.ReviewNoLongerChallenged:
                await dbContext.Attempts
                    .Where(attempt => attempt.Id == review.Attempt.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(attempt => attempt.AgentOutcome, AgentOutcome.Accepted));
                break;
        }

        await using var verification = _fixture.CreateContext();
        var result = await EvaluateAsync(verification, scene, resolution.RevisedProposal.Id);

        Assert.Null(result.Node);
        Assert.NotNull(result.Failure);
        Assert.All(verification.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task The_exact_acceptance_is_found_only_for_an_accepted_review_bound_to_the_lineage()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var accepted = seeder.AddReview(first.RevisedProposal, AgentOutcome.Accepted);
        var challenged = seeder.AddReview(root, AgentOutcome.Challenged);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, scene.Run.Id, CancellationToken.None);

        var acceptance = PlanningLineage.FindExactAcceptance(
            snapshot, accepted.Attempt, scene.Run.Id, first.RevisedProposal.Id, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint);
        Assert.Equal(accepted.Outputs[0].Id, acceptance?.Id);

        Assert.Null(PlanningLineage.FindExactAcceptance(
            snapshot, accepted.Attempt, scene.Run.Id, first.RevisedProposal.Id, scene.Workspace.Id, Guid.NewGuid(), Fingerprint));
        Assert.Null(PlanningLineage.FindExactAcceptance(
            snapshot, challenged.Attempt, scene.Run.Id, root.Id, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint));
        Assert.Null(PlanningLineage.FindExactAcceptance(
            snapshot, accepted.Attempt, scene.Run.Id, root.Id, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint));
    }

    // The downstream review and correction workflows re-validate an implementation attempt's own
    // recorded input chain; it must agree with the claim about which plans are implementable.
    [Theory]
    [InlineData(ChainShape.RevisionWithDecisionsOnly, true)]
    [InlineData(ChainShape.RevisionWithDecisionsAndAcceptedSecondReview, true)]
    [InlineData(ChainShape.RevisionWithAcceptanceOfAnotherProposal, false)]
    [InlineData(ChainShape.RevisionWithUnexpectedTrailingMessage, false)]
    [InlineData(ChainShape.DepthTwoRevision, false)]
    public async Task An_implementation_input_chain_is_valid_only_for_a_first_revision_with_its_exact_evidence(
        ChainShape shape, bool expectedValid)
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);

        var planMessage = first.RevisedProposal;
        var evidenceIds = first.Decisions.Select(decision => decision.Id).ToList();
        switch (shape)
        {
            case ChainShape.RevisionWithDecisionsAndAcceptedSecondReview:
                evidenceIds.Add(seeder.AddReview(planMessage, AgentOutcome.Accepted).Outputs[0].Id);
                break;
            case ChainShape.RevisionWithAcceptanceOfAnotherProposal:
                evidenceIds.Add(seeder.AddReview(root, AgentOutcome.Accepted).Outputs[0].Id);
                break;
            case ChainShape.RevisionWithUnexpectedTrailingMessage:
                evidenceIds.Add(root.Id);
                break;
            case ChainShape.DepthTwoRevision:
                var (_, second) = seeder.AddChallengedRound(planMessage);
                planMessage = second.RevisedProposal;
                evidenceIds = second.Decisions.Select(decision => decision.Id).ToList();
                break;
        }

        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);
        var number = seeder.NextAttemptNumber;
        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), scene.Run.Id, number, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(implementation);
        var orderedInputs = new List<Guid> { planMessage.Id };
        orderedInputs.AddRange(evidenceIds);
        for (var index = 0; index < orderedInputs.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, orderedInputs[index], index));
        }

        var report = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.ExecutionReport, planMessage.Id, "Implemented the plan.",
            "{\"completedWork\":\"Applied the plan\",\"verification\":\"Tests passed\"}", Now);
        dbContext.CollaborationMessages.Add(report);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, report, scene.Run.Id, scene.Workspace.Id, resultCheckpoint.Id, CancellationToken.None);

        Assert.Equal(expectedValid, result is not null);
        if (result is not null)
        {
            Assert.Equal(root.Id, result.OriginalProposal.Id);
        }
    }

    public enum ChainShape
    {
        RevisionWithDecisionsOnly,
        RevisionWithDecisionsAndAcceptedSecondReview,
        RevisionWithAcceptanceOfAnotherProposal,
        RevisionWithUnexpectedTrailingMessage,
        DepthTwoRevision,
    }
}
