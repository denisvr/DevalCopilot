using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// What a claimed format repair may produce. It is an ordinary fresh attempt of its role: the unchanged result
/// handlers alone record a valid result (Acceptance or Challenges, Decisions plus a revised Proposal, or review
/// results and findings), a repair that is invalid again records nothing semantic and cannot itself be repaired,
/// and a repair adds no challenge round, no automatic follow-up, and no second escalation.
/// </summary>
public sealed class ReadOnlyFormatRepairResultTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static readonly IReadOnlyList<SealedCriticalReviewArtifact> NoCriticalArtifacts = [];
    private static readonly IReadOnlyList<SealedChallengeResolutionArtifact> NoResolutionArtifacts = [];
    private static readonly IReadOnlyList<SealedImplementationReviewArtifact> NoReviewArtifacts = [];

    private RepairEvidenceReader Reader(string fingerprint) => RepairEvidenceReader.Matching(fingerprint);

    private async Task<Guid> ClaimCriticalRepairAsync(RepairTestScene scene, Attempt source)
    {
        var result = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                scene.Db, Reader(RepairTestScene.Fingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value.AttemptId;
    }

    private async Task<Guid> ClaimResolutionRepairAsync(RepairTestScene scene, Attempt source)
    {
        var result = await new CreateChallengeResolutionAttemptCommandHandler(
                scene.Db, Reader(RepairTestScene.Fingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value.AttemptId;
    }

    private async Task<Guid> ClaimCodeReviewRepairAsync(RepairTestScene scene, Attempt source, string fingerprint)
    {
        var result = await new CreateCodeReviewAttemptCommandHandler(
                scene.Db, Reader(fingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value.AttemptId;
    }

    private async Task DispatchAsync(Guid runId, Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        var dispatched = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(1)))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
        Assert.True(dispatched.IsSuccess, dispatched.IsFailure ? dispatched.Errors[0].Code : null);
        await context.SaveChangesAsync();
    }

    private static ValidatedCriticalReview Challenges(int count) => ValidatedCriticalReview.ForChallenges(
        Enumerable.Range(1, count).Select(index => new ValidatedChallenge(
            $"Challenge {index} summary",
            JsonSerializer.Serialize(new
            {
                disputedItem = $"Disputed item {index}",
                materialImpact = $"Material impact {index}",
                reasoning = $"Reasoning {index}",
                alternativeOrQuestion = $"Alternative or question {index}",
            }))).ToList(),
        "The proposal has material gaps.");

    private static ValidatedChallengeResolution ResolutionFor(IEnumerable<Guid> challengeIds) =>
        ValidatedChallengeResolution.Create(
            "Overall resolution summary",
            challengeIds.Select((id, index) => new ValidatedDecision(
                id,
                $"Decision {index + 1} summary",
                JsonSerializer.Serialize(new
                {
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = $"Rationale {index + 1}",
                    resultingPlanChanges = $"Plan changes {index + 1}",
                    nextAction = $"Next action {index + 1}",
                }))).ToList(),
            new ValidatedRevisedProposal("Revised proposal summary", PlanningLineageSeeder.ProposalJson("Revised scope")));

    [Fact]
    public async Task A_critical_review_repair_records_an_ordinary_acceptance_and_leaves_the_source_failed_and_message_free()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, proposal) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var repairId = await ClaimCriticalRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);

        await using var context = _fixture.CreateContext();
        var result = await new RecordClaudeCriticalReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
            .HandleAsync(
                new RecordClaudeCriticalReviewResultCommand(
                    scene.Run.Id, repairId, AgentOutcome.Accepted, RepairTestScene.Fingerprint, NoCriticalArtifacts,
                    ValidatedCriticalReview.ForAcceptance(new ValidatedAcceptance(
                        "The proposal is sound.", JsonSerializer.Serialize(new { rationale = "Matches the stated risks." }))),
                    null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == repairId);
        Assert.Equal(AttemptStatus.Completed, repair.Status);
        Assert.Equal(AgentOutcome.Accepted, repair.AgentOutcome);
        Assert.Equal(source.Id, repair.AgentRepairSourceAttemptId);
        var acceptance = Assert.Single(verify.CollaborationMessages.Where(m => m.AttemptId == repairId));
        Assert.Equal(CollaborationMessageType.Acceptance, acceptance.Type);
        Assert.Equal(proposal.Id, acceptance.InReplyToMessageId);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == source.Id));
        var persistedSource = await verify.Attempts.SingleAsync(a => a.Id == source.Id);
        Assert.Equal(AttemptStatus.Failed, persistedSource.Status);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedSource.AgentOutcome);
        // No follow-up of any kind: only the source, the repair, and the seeded root exist.
        Assert.Equal(3, verify.Attempts.Count(a => a.RunId == scene.Run.Id));
    }

    [Fact]
    public async Task A_critical_review_repair_of_a_revised_proposal_records_its_challenges_as_an_ordinary_review()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, revised) = scene.AddCriticalReviewSource(reviseFirst: true);
        await scene.SaveAsync();
        var repairId = await ClaimCriticalRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);

        await using var context = _fixture.CreateContext();
        var result = await new RecordClaudeCriticalReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
            .HandleAsync(
                new RecordClaudeCriticalReviewResultCommand(
                    scene.Run.Id, repairId, AgentOutcome.Challenged, RepairTestScene.Fingerprint, NoCriticalArtifacts, Challenges(2), null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var challenges = verify.CollaborationMessages.Where(m => m.AttemptId == repairId).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, challenges.Count);
        Assert.All(challenges, challenge => Assert.Equal(revised.Id, challenge.InReplyToMessageId));
        Assert.All(challenges, challenge => Assert.Equal(CollaborationMessageType.Challenge, challenge.Type));
    }

    [Fact]
    public async Task An_invalid_critical_review_repair_records_no_semantic_message_and_cannot_be_repaired_again()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var repairId = await ClaimCriticalRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);

        await using (var context = _fixture.CreateContext())
        {
            var result = await new RecordClaudeCriticalReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
                .HandleAsync(
                    new RecordClaudeCriticalReviewResultCommand(
                        scene.Run.Id, repairId, AgentOutcome.InvalidStructuredOutput, RepairTestScene.Fingerprint, NoCriticalArtifacts, null,
                        null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                    CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == repairId);
        Assert.Equal(AttemptStatus.Failed, repair.Status);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, repair.AgentOutcome);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == source.Id));

        // Neither the repair nor its source can be repaired: no chain, no second repair.
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            verify, Reader(RepairTestScene.Fingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
            new AttemptDurabilityProbe(_fixture.Options));
        var ofRepair = await handler.HandleAsync(
            CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, repairId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_of_repair_forbidden", Assert.Single(ofRepair.Errors).Code);
        var ofSource = await handler.HandleAsync(
            CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_already_requested", Assert.Single(ofSource.Errors).Code);
    }

    [Fact]
    public async Task A_first_round_resolution_repair_records_decisions_and_a_revised_proposal_without_an_escalation()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, proposal) = scene.AddResolverSource(challengeCount: 2);
        await scene.SaveAsync();
        var repairId = await ClaimResolutionRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);

        await using var context = _fixture.CreateContext();
        var result = await new RecordChallengeResolutionResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
            .HandleAsync(
                new RecordChallengeResolutionResultCommand(
                    scene.Run.Id, repairId, AgentOutcome.Resolved, RepairTestScene.Fingerprint, NoResolutionArtifacts,
                    ResolutionFor(review.Outputs.Select(challenge => challenge.Id)), null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var recorded = verify.CollaborationMessages.Where(m => m.AttemptId == repairId).ToList();
        Assert.Equal(2, recorded.Count(m => m.Type == CollaborationMessageType.Decision));
        Assert.Equal(proposal.Id, Assert.Single(recorded, m => m.Type == CollaborationMessageType.Proposal).InReplyToMessageId);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.RunId == scene.Run.Id && m.Type == CollaborationMessageType.Escalation));
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == source.Id));
    }

    [Fact]
    public async Task A_second_round_resolution_repair_records_exactly_one_depth_two_escalation_and_adds_no_challenge_round()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, revised) = scene.AddResolverSource(secondRound: true, challengeCount: 2);
        await scene.SaveAsync();
        var repairId = await ClaimResolutionRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);
        var command = new RecordChallengeResolutionResultCommand(
            scene.Run.Id, repairId, AgentOutcome.Resolved, RepairTestScene.Fingerprint, NoResolutionArtifacts,
            ResolutionFor(review.Outputs.Select(challenge => challenge.Id)), null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);

        await using (var context = _fixture.CreateContext())
        {
            var result = await new RecordChallengeResolutionResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
                .HandleAsync(command, CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        // A repeated recording of the same repair is refused and adds no second escalation.
        await using (var context = _fixture.CreateContext())
        {
            var repeated = await new RecordChallengeResolutionResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(3)))
                .HandleAsync(command, CancellationToken.None);
            Assert.True(repeated.IsFailure);
        }

        await using var verify = _fixture.CreateContext();
        var revisedProposal = Assert.Single(verify.CollaborationMessages.Where(
            m => m.AttemptId == repairId && m.Type == CollaborationMessageType.Proposal));
        Assert.Equal(revised.Id, revisedProposal.InReplyToMessageId);
        var escalation = Assert.Single(verify.CollaborationMessages.Where(m => m.RunId == scene.Run.Id && m.Type == CollaborationMessageType.Escalation));
        Assert.Equal(revisedProposal.Id, escalation.InReplyToMessageId);
        // The seeded first-round resolution, the failed second-round source, and its one repair; no third review.
        Assert.Equal(3, verify.Attempts.Count(a => a.RunId == scene.Run.Id && a.AgentRole == AgentRole.Resolver));
        Assert.Equal(2, verify.Attempts.Count(a => a.RunId == scene.Run.Id && a.AgentRole == AgentRole.CriticalReviewer));
    }

    [Fact]
    public async Task An_invalid_resolution_repair_records_nothing_semantic_and_no_escalation()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource(secondRound: true);
        await scene.SaveAsync();
        var repairId = await ClaimResolutionRepairAsync(scene, source);
        await DispatchAsync(scene.Run.Id, repairId);

        await using (var context = _fixture.CreateContext())
        {
            var result = await new RecordChallengeResolutionResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
                .HandleAsync(
                    new RecordChallengeResolutionResultCommand(
                        scene.Run.Id, repairId, AgentOutcome.InvalidStructuredOutput, RepairTestScene.Fingerprint, NoResolutionArtifacts,
                        null, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                    CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await verify.Attempts.SingleAsync(a => a.Id == repairId)).AgentOutcome);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == source.Id));
        Assert.Empty(verify.CollaborationMessages.Where(m => m.RunId == scene.Run.Id && m.Type == CollaborationMessageType.Escalation));
        var again = await new CreateChallengeResolutionAttemptCommandHandler(
                verify, Reader(RepairTestScene.Fingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, repairId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_of_repair_forbidden", Assert.Single(again.Errors).Code);
    }

    [Fact]
    public async Task A_code_review_repair_records_an_ordinary_approval_and_its_checkpoint_review()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        var repairId = await ClaimCodeReviewRepairAsync(scene, source, implementation.ReviewFingerprint);
        await DispatchAsync(scene.Run.Id, repairId);

        await using var context = _fixture.CreateContext();
        var result = await new RecordImplementationReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
            .HandleAsync(
                new RecordImplementationReviewResultCommand(
                    scene.Run.Id, repairId, AgentOutcome.ReviewApproved, implementation.ReviewFingerprint, NoReviewArtifacts,
                    ValidatedImplementationReview.CreateApproved("All good.", "Follows the plan.", "None material."), null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        await using var verify = _fixture.CreateContext();
        var approval = Assert.Single(verify.CollaborationMessages.Where(m => m.AttemptId == repairId));
        Assert.Equal(CollaborationMessageType.ReviewApproval, approval.Type);
        Assert.Equal(implementation.ExecutionReport.Id, approval.InReplyToMessageId);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == source.Id));
        Assert.Single(verify.CheckpointReviews.Where(r => r.GitCheckpointId == implementation.ReviewCheckpoint.Id));
        // No implementation authority or correction follows from a repair alone.
        Assert.Empty(verify.Attempts.Where(a => a.RunId == scene.Run.Id && a.AgentRole == AgentRole.Implementer && a.AttemptNumber > source.AttemptNumber));
    }

    [Fact]
    public async Task A_correction_report_code_review_repair_records_ordinary_findings()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddCorrectedImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation, commandCount: 1);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        var repairId = await ClaimCodeReviewRepairAsync(scene, source, implementation.ReviewFingerprint);
        await DispatchAsync(scene.Run.Id, repairId);
        var findings = new[]
        {
            new ValidatedReviewFinding("high", "correctness", "Finding 1", "Evidence 1", "Change 1", "src/Foo.cs"),
            new ValidatedReviewFinding("low", "standards", "Finding 2", "Evidence 2", "Change 2", null),
        };

        await using var context = _fixture.CreateContext();
        var result = await new RecordImplementationReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
            .HandleAsync(
                new RecordImplementationReviewResultCommand(
                    scene.Run.Id, repairId, AgentOutcome.ReviewChangesRequested, implementation.ReviewFingerprint, NoReviewArtifacts,
                    ValidatedImplementationReview.CreateChangesRequested("Needs work.", findings), null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        await using var verify = _fixture.CreateContext();
        var recorded = verify.CollaborationMessages.Where(m => m.AttemptId == repairId).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, recorded.Count);
        Assert.All(recorded, message => Assert.Equal(CollaborationMessageType.ReviewFinding, message.Type));
        Assert.All(recorded, message => Assert.Equal(implementation.ExecutionReport.Id, message.InReplyToMessageId));
    }

    [Fact]
    public async Task An_invalid_code_review_repair_records_nothing_semantic_and_cannot_be_repaired_again()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        var repairId = await ClaimCodeReviewRepairAsync(scene, source, implementation.ReviewFingerprint);
        await DispatchAsync(scene.Run.Id, repairId);

        await using (var context = _fixture.CreateContext())
        {
            var result = await new RecordImplementationReviewResultCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(2)))
                .HandleAsync(
                    new RecordImplementationReviewResultCommand(
                        scene.Run.Id, repairId, AgentOutcome.InvalidStructuredOutput, implementation.ReviewFingerprint, NoReviewArtifacts,
                        null, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                    CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, (await verify.Attempts.SingleAsync(a => a.Id == repairId)).AgentOutcome);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == source.Id));
        Assert.Empty(verify.CheckpointReviews.Where(r => r.GitCheckpointId == implementation.ReviewCheckpoint.Id));
        var again = await new CreateCodeReviewAttemptCommandHandler(
                verify, Reader(implementation.ReviewFingerprint), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, repairId), CancellationToken.None);
        Assert.Equal("agent_attempts.repair_of_repair_forbidden", Assert.Single(again.Errors).Code);
    }
}
