using System.Data.Common;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The result boundary of a challenge resolution: the first resolution records Decisions and
/// a revised Proposal; the second and final one additionally records exactly one bounded,
/// orchestrator-authored human escalation in the same atomic save. Inconsistent, exhausted, or
/// competing evidence is refused with nothing recorded.</summary>
public sealed class SecondChallengeRoundResultTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<SealedChallengeResolutionArtifact> NoArtifacts = [];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>A claimed, dispatched, still-Running resolver attempt whose ordered inputs are the
    /// reviewed Proposal followed by the given challenges.</summary>
    private static Attempt AddRunningResolver(
        DevalCopilotDbContext dbContext, Scene scene, int number, CollaborationMessage reviewed, IEnumerable<CollaborationMessage> challenges)
    {
        var resolver = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), scene.Run.Id, number, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        resolver.MarkAgentDispatched(Now);
        dbContext.Attempts.Add(resolver);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolver.Id, reviewed.Id, sequence: 0));
        var sequence = 1;
        foreach (var challenge in challenges)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolver.Id, challenge.Id, sequence++));
        }

        return resolver;
    }

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
            new ValidatedRevisedProposal(
                "Second revised proposal summary",
                PlanningLineageSeeder.ProposalJson("Second revised scope")));

    private static Task<Devalente.Shared.Results.Result<RecordChallengeResolutionResultCommandResult>> RecordAsync(
        DevalCopilotDbContext dbContext, Attempt attempt, IEnumerable<Guid> challengeIds, string fingerprint = "") =>
        new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordChallengeResolutionResultCommand(
                attempt.RunId, attempt.Id, AgentOutcome.Resolved, fingerprint.Length == 0 ? Fingerprint : fingerprint,
                NoArtifacts, ResolutionFor(challengeIds), null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

    private static IQueryable<CollaborationMessage> Escalations(DevalCopilotDbContext dbContext, Guid runId) =>
        dbContext.CollaborationMessages.Where(message => message.RunId == runId && message.Type == CollaborationMessageType.Escalation);

    [Fact]
    public async Task The_first_resolution_records_its_decisions_and_revised_proposal_and_no_escalation()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var review = seeder.AddReview(root, AgentOutcome.Challenged, challengeCount: 2);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, root, review.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await RecordAsync(dbContext, resolver, review.Outputs.Select(challenge => challenge.Id));

        Assert.True(result.IsSuccess);
        var recorded = dbContext.CollaborationMessages.Where(message => message.AttemptId == resolver.Id).ToList();
        Assert.Equal(2, recorded.Count(message => message.Type == CollaborationMessageType.Decision));
        Assert.Equal(root.Id, Assert.Single(recorded, message => message.Type == CollaborationMessageType.Proposal).InReplyToMessageId);
        Assert.Empty(Escalations(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task The_second_resolution_records_decisions_a_depth_two_proposal_and_exactly_one_bounded_escalation()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged, challengeCount: 3);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var challengeIds = secondReview.Outputs.Select(challenge => challenge.Id).ToList();

        var result = await RecordAsync(dbContext, resolver, challengeIds);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.Resolved, resolver.AgentOutcome);
        var recorded = dbContext.CollaborationMessages.Where(message => message.AttemptId == resolver.Id).OrderBy(message => message.Sequence).ToList();
        Assert.Equal(3, recorded.Count(message => message.Type == CollaborationMessageType.Decision));
        var revised = Assert.Single(recorded, message => message.Type == CollaborationMessageType.Proposal);
        Assert.Equal(first.RevisedProposal.Id, revised.InReplyToMessageId);

        var escalation = Assert.Single(Escalations(dbContext, scene.Run.Id));
        Assert.Equal(revised.Id, escalation.InReplyToMessageId);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, escalation.Provenance);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), escalation.Actor);
        Assert.Equal(ParticipantIdentity.ForHuman(), escalation.Recipient);
        Assert.Null(escalation.AttemptId);
        Assert.True(escalation.Sequence > revised.Sequence);
        Assert.Equal(PlanningEscalation.Summary, escalation.Summary);

        // Bounded metadata: the lineage and the exact resolved challenges by identifier, and no
        // provider or artifact text (decision rationale, proposal scope, or challenge wording).
        using var content = JsonDocument.Parse(escalation.StructuredContentJson);
        var evidence = content.RootElement.GetProperty("evidence").GetString()!;
        Assert.Contains(root.Id.ToString(), evidence);
        Assert.Contains(first.RevisedProposal.Id.ToString(), evidence);
        Assert.Contains(revised.Id.ToString(), evidence);
        Assert.All(challengeIds, id => Assert.Contains(id.ToString(), evidence));
        Assert.Contains("3 second-round challenge(s)", evidence);
        Assert.True(escalation.StructuredContentJson.Length < CollaborationMessageContentPolicy.MaximumStructuredContentLength);
        foreach (var forbidden in new[] { "Rationale", "Second revised scope", "Disputed item", "Decision 1 summary" })
        {
            Assert.DoesNotContain(forbidden, escalation.StructuredContentJson);
        }

        var events = dbContext.Events.Where(runEvent => runEvent.AttemptId == resolver.Id).OrderBy(runEvent => runEvent.Sequence).ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), events[^1].Actor);
        Assert.Equal(result.Value.LatestEventSequence, events[^1].Sequence);
        Assert.Contains(escalation.Id.ToString(), events[^1].PayloadJson);
    }

    [Fact]
    public async Task A_recorded_second_resolution_cannot_be_recorded_again_so_the_escalation_stays_single()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var challengeIds = secondReview.Outputs.Select(challenge => challenge.Id).ToList();

        var firstRecording = await RecordAsync(dbContext, resolver, challengeIds);
        var secondRecording = await RecordAsync(dbContext, resolver, challengeIds);

        Assert.True(firstRecording.IsSuccess);
        Assert.Equal("attempts.not_active", Assert.Single(secondRecording.Errors).Code);
        Assert.Single(Escalations(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task A_second_resolution_that_drifted_from_the_checkpoint_records_neither_decisions_nor_escalation()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await RecordAsync(dbContext, resolver, secondReview.Outputs.Select(c => c.Id), fingerprint: new string('f', 64));

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.SourceChanged, resolver.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == resolver.Id));
        Assert.Empty(Escalations(dbContext, scene.Run.Id));
    }

    [Fact]
    public async Task A_commit_failure_records_no_partial_decision_proposal_or_escalation_set()
    {
        await using var seedContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(seedContext);
        var seeder = SeederFor(seedContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root, challengeCount: 2);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged, challengeCount: 2);
        var resolver = AddRunningResolver(seedContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs);
        await seedContext.SaveChangesAsync(CancellationToken.None);

        await using (var failing = _fixture.CreateContext(new FailingCommitInterceptor()))
        {
            var attempt = await failing.Attempts.SingleAsync(candidate => candidate.Id == resolver.Id);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                RecordAsync(failing, attempt, secondReview.Outputs.Select(challenge => challenge.Id)));
        }

        await using var verification = _fixture.CreateContext();
        Assert.Empty(verification.CollaborationMessages.Where(message => message.AttemptId == resolver.Id));
        Assert.Empty(Escalations(verification, scene.Run.Id));
        var persisted = await verification.Attempts.SingleAsync(candidate => candidate.Id == resolver.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
    }

    [Fact]
    public async Task A_resolution_of_a_depth_two_proposal_is_refused_with_nothing_recorded()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var (_, second) = seeder.AddChallengedRound(first.RevisedProposal);
        var impossibleReview = seeder.AddReview(second.RevisedProposal, AgentOutcome.Challenged);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, second.RevisedProposal, impossibleReview.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await RecordAsync(dbContext, resolver, impossibleReview.Outputs.Select(challenge => challenge.Id));

        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(result.Errors).Code);
        await AssertNothingRecordedAsync(scene, resolver);
    }

    [Fact]
    public async Task A_resolution_that_does_not_cover_the_reviews_complete_challenge_set_is_refused()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged, challengeCount: 3);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs.Take(2));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await RecordAsync(dbContext, resolver, secondReview.Outputs.Take(2).Select(challenge => challenge.Id));

        Assert.Equal("agent_attempts.proposal_lineage_not_valid", Assert.Single(result.Errors).Code);
        await AssertNothingRecordedAsync(scene, resolver);
    }

    [Fact]
    public async Task A_resolution_is_refused_when_another_attempt_already_resolved_the_same_challenge_set()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var secondReview = seeder.AddReview(first.RevisedProposal, AgentOutcome.Challenged);
        seeder.AddResolution(first.RevisedProposal, secondReview.Outputs);
        var resolver = AddRunningResolver(dbContext, scene, seeder.NextAttemptNumber, first.RevisedProposal, secondReview.Outputs);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await RecordAsync(dbContext, resolver, secondReview.Outputs.Select(challenge => challenge.Id));

        Assert.Equal("agent_attempts.already_resolved", Assert.Single(result.Errors).Code);
        await AssertNothingRecordedAsync(scene, resolver);
    }

    private async Task AssertNothingRecordedAsync(Scene scene, Attempt resolver)
    {
        await using var verification = _fixture.CreateContext();
        Assert.Empty(verification.CollaborationMessages.Where(message => message.AttemptId == resolver.Id));
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == resolver.Id));
        Assert.Empty(Escalations(verification, scene.Run.Id));
        var persisted = await verification.Attempts.SingleAsync(candidate => candidate.Id == resolver.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
    }

    /// <summary>Fails the commit of the save it is attached to, after every statement was sent, so
    /// the whole unit must roll back.</summary>
    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected commit failure.");
    }
}
