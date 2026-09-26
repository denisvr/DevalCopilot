using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Proves the recorded-collaboration-input-provenance slice: the drill-down's new
/// <see cref="CollaborationMessageEvidenceQueryResult.InputMessagesStatus"/>/<see cref="CollaborationMessageEvidenceQueryResult.InputMessages"/>
/// fields resolve strictly through this attempt's own durable <see cref="AttemptInputMessage"/>
/// rows, preserve their stored order, resolve every reference only within the requesting run, apply
/// a small bounded cap with an honest omission signal, and fail the entire set closed — never a
/// partially trusted subset — for a gap, a duplicate, or a missing/foreign reference. Every existing
/// <see cref="CollaborationMessageEvidenceStatus"/> behavior is unaffected; see
/// <see cref="GetCollaborationMessageEvidenceQueryHandlerTests"/> for that coverage.
/// </summary>
public sealed class AttemptInputMessageEvidenceTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

    private const string ChallengeContent =
        "{\"disputedItem\":\"Disputed item\",\"materialImpact\":\"Material impact\",\"reasoning\":\"Reasoning\",\"alternativeOrQuestion\":\"Alternative or question\"}";

    private static Attempt ClaimDispatchedPlannerAttempt(Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        attempt.MarkAgentDispatched(claimedAtUtc.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedCriticalReviewAttempt(Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc)
    {
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        attempt.MarkAgentDispatched(claimedAtUtc.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedResolverAttempt(Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc)
    {
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, claimedAtUtc, attemptNumber);
        attempt.MarkAgentDispatched(claimedAtUtc.AddSeconds(1));
        return attempt;
    }

    private static CollaborationMessage RecordProposal(Attempt attempt, DateTimeOffset occurredAtUtc, Guid? inReplyTo = null) =>
        CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, inReplyTo, "A proposal", ProposalContent, occurredAtUtc);

    private static CollaborationMessage RecordChallenge(Attempt attempt, Guid proposalId, DateTimeOffset occurredAtUtc) =>
        CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Challenge, proposalId, "A challenge", ChallengeContent, occurredAtUtc);

    private static CollaborationMessage RecordRevisedProposal(Attempt resolverAttempt, Guid inReplyToProposalId, DateTimeOffset occurredAtUtc) =>
        CollaborationMessage.RecordAgent(
            resolverAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, inReplyToProposalId, "A revised proposal", ProposalContent, occurredAtUtc);

    [Fact]
    public async Task A_planner_attempt_with_no_recorded_inputs_reports_Empty()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No recorded inputs", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Empty, result.Value.InputMessagesStatus);
        Assert.Empty(result.Value.InputMessages);
        Assert.False(result.Value.InputMessagesOmitted);
        Assert.Equal(0, result.Value.InputMessageTotalCount);
    }

    [Fact]
    public async Task A_resolver_attempts_ordered_proposal_and_challenges_are_reported_coherently_in_stored_order()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Coherent ordered inputs", Now);
        run.Claim(Now);

        // Only one Agent attempt may be Running per run at a time, so every supporting attempt is
        // completed before the next is claimed — its own outcome is irrelevant to this test, only
        // its authored messages and this attempt's own AttemptInputMessage rows are.
        var plannerAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var proposal = RecordProposal(plannerAttempt, Now.AddSeconds(1));
        plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(1), TestProcessEvidence.CleanExit);

        var reviewAttempt = ClaimDispatchedCriticalReviewAttempt(run.Id, 2, Now.AddSeconds(2));
        var challengeOne = RecordChallenge(reviewAttempt, proposal.Id, Now.AddSeconds(3));
        var challengeTwo = RecordChallenge(reviewAttempt, proposal.Id, Now.AddSeconds(4));
        reviewAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now.AddSeconds(4), TestProcessEvidence.CleanExit);

        var resolverAttempt = ClaimDispatchedResolverAttempt(run.Id, 3, Now.AddSeconds(5));
        var revisedProposal = RecordRevisedProposal(resolverAttempt, proposal.Id, Now.AddSeconds(6));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(plannerAttempt, reviewAttempt, resolverAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, challengeOne, challengeTwo, revisedProposal);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challengeOne.Id, sequence: 1),
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challengeTwo.Id, sequence: 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Recorded, result.Value.InputMessagesStatus);
        Assert.False(result.Value.InputMessagesOmitted);
        Assert.Equal(3, result.Value.InputMessageTotalCount);
        Assert.Equal(3, result.Value.InputMessages.Count);

        // Preserves the exact stored order — never re-sorted by type, time, or anything else.
        Assert.Equal([0, 1, 2], result.Value.InputMessages.Select(entry => entry.Sequence));
        Assert.Equal([proposal.Id, challengeOne.Id, challengeTwo.Id], result.Value.InputMessages.Select(entry => entry.CollaborationMessageId));
        Assert.Equal(CollaborationMessageType.Proposal, result.Value.InputMessages[0].Type);
        Assert.Equal(CollaborationMessageType.Challenge, result.Value.InputMessages[1].Type);
        Assert.Equal(CollaborationMessageType.Challenge, result.Value.InputMessages[2].Type);
    }

    [Fact]
    public async Task A_gap_in_the_stored_sequence_fails_the_entire_set_closed()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Gapped inputs", Now);
        run.Claim(Now);

        var plannerAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var proposal = RecordProposal(plannerAttempt, Now.AddSeconds(1));
        plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(1), TestProcessEvidence.CleanExit);

        var reviewAttempt = ClaimDispatchedCriticalReviewAttempt(run.Id, 2, Now.AddSeconds(2));
        var challenge = RecordChallenge(reviewAttempt, proposal.Id, Now.AddSeconds(3));
        reviewAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now.AddSeconds(3), TestProcessEvidence.CleanExit);

        var resolverAttempt = ClaimDispatchedResolverAttempt(run.Id, 3, Now.AddSeconds(4));
        var revisedProposal = RecordRevisedProposal(resolverAttempt, proposal.Id, Now.AddSeconds(5));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(plannerAttempt, reviewAttempt, resolverAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, challenge, revisedProposal);
        // Sequence 1 is missing (0, then 2) — a gap that should be impossible under the real
        // recording path, reproduced directly to prove the read side fails closed rather than
        // silently reindexing or dropping the gap.
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challenge.Id, sequence: 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Invalid, result.Value.InputMessagesStatus);
        Assert.Empty(result.Value.InputMessages);
        // The real row count is still reported honestly — a count is not content.
        Assert.Equal(2, result.Value.InputMessageTotalCount);
    }

    [Fact]
    public async Task A_reference_to_a_message_recorded_under_a_different_run_fails_the_entire_set_closed()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Cross-run input reference", Now);
        var otherRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "A different run", Now);
        run.Claim(Now);
        otherRun.Claim(Now);

        var plannerAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var proposal = RecordProposal(plannerAttempt, Now.AddSeconds(1));
        plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(1), TestProcessEvidence.CleanExit);

        // A real, persisted message — but recorded under otherRun, not run.
        var otherRunPlannerAttempt = ClaimDispatchedPlannerAttempt(otherRun.Id, 1, Now.AddSeconds(2));
        var foreignMessage = RecordProposal(otherRunPlannerAttempt, Now.AddSeconds(3));

        var resolverAttempt = ClaimDispatchedResolverAttempt(run.Id, 2, Now.AddSeconds(4));
        var revisedProposal = RecordRevisedProposal(resolverAttempt, proposal.Id, Now.AddSeconds(5));

        dbContext.Projects.Add(project);
        dbContext.Runs.AddRange(run, otherRun);
        dbContext.Attempts.AddRange(plannerAttempt, otherRunPlannerAttempt, resolverAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, foreignMessage, revisedProposal);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
            // Sequence 1 references a message that is real, but belongs to otherRun — never a
            // trustworthy same-run input for this run's own attempt.
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, foreignMessage.Id, sequence: 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Invalid, result.Value.InputMessagesStatus);
        Assert.Empty(result.Value.InputMessages);
        Assert.Equal(2, result.Value.InputMessageTotalCount);
    }

    [Fact]
    public async Task A_reference_to_a_nonexistent_message_fails_the_entire_set_closed()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Missing input reference", Now);
        run.Claim(Now);

        var plannerAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var proposal = RecordProposal(plannerAttempt, Now.AddSeconds(1));
        plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(1), TestProcessEvidence.CleanExit);

        var resolverAttempt = ClaimDispatchedResolverAttempt(run.Id, 2, Now.AddSeconds(2));
        var revisedProposal = RecordRevisedProposal(resolverAttempt, proposal.Id, Now.AddSeconds(3));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(plannerAttempt, resolverAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, revisedProposal);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
            // Sequence 1 references a message id that does not exist at all — a dangling reference.
            AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, Guid.NewGuid(), sequence: 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Invalid, result.Value.InputMessagesStatus);
        Assert.Empty(result.Value.InputMessages);
        Assert.Equal(2, result.Value.InputMessageTotalCount);
    }

    [Fact]
    public async Task A_coherent_set_larger_than_the_bounded_cap_is_capped_with_an_honest_omission_signal()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Many recorded inputs", Now);
        run.Claim(Now);

        var plannerAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var proposal = RecordProposal(plannerAttempt, Now.AddSeconds(1));
        plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(1), TestProcessEvidence.CleanExit);

        var reviewAttempt = ClaimDispatchedCriticalReviewAttempt(run.Id, 2, Now.AddSeconds(2));

        // 11 total input rows — one more than the handler's bounded cap (10) — built from real,
        // distinct persisted Challenge messages rather than a hypothetical count.
        const int totalInputs = 11;
        var challenges = new List<CollaborationMessage>();
        for (var sequence = 1; sequence < totalInputs; sequence++)
        {
            challenges.Add(RecordChallenge(reviewAttempt, proposal.Id, Now.AddSeconds(2 + sequence)));
        }

        reviewAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now.AddSeconds(2 + totalInputs), TestProcessEvidence.CleanExit);

        var resolverAttempt = ClaimDispatchedResolverAttempt(run.Id, 3, Now.AddSeconds(3 + totalInputs));
        var revisedProposal = RecordRevisedProposal(resolverAttempt, proposal.Id, Now.AddSeconds(4 + totalInputs));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(plannerAttempt, reviewAttempt, resolverAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, revisedProposal);
        dbContext.CollaborationMessages.AddRange(challenges);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0));
        for (var index = 0; index < challenges.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challenges[index].Id, sequence: index + 1));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationMessageEvidenceQuery(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptInputMessageEvidenceStatus.Recorded, result.Value.InputMessagesStatus);
        Assert.Equal(10, result.Value.InputMessages.Count);
        Assert.True(result.Value.InputMessagesOmitted);
        Assert.Equal(totalInputs, result.Value.InputMessageTotalCount);
        // The bounded prefix is still the exact stored order, never an arbitrary subset.
        Assert.Equal(Enumerable.Range(0, 10), result.Value.InputMessages.Select(entry => entry.Sequence));
    }

    [Fact]
    public void The_query_result_never_structurally_carries_any_excluded_evidence_field()
    {
        var excludedNames = new[] { "Summary", "StructuredContentJson", "RawResponse", "ContextManifestContent" };
        var propertyNames = typeof(AttemptInputMessageEvidence).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var excluded in excludedNames)
        {
            Assert.DoesNotContain(excluded, propertyNames);
        }
    }
}
