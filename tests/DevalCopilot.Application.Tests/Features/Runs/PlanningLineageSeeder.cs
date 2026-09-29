using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Seeds real, durable Proposal → Challenge → Decision → revised Proposal lineage evidence: a
/// completed Planner attempt and its Proposal, completed critical-review attempts (Accepted or
/// Challenged) with their ordered inputs and messages, and completed Resolver attempts with their
/// ordered inputs, one Decision per Challenge, and the revised Proposal. Every attempt is bound to
/// the given workspace, checkpoint, and fingerprint and gets its own attempt number and budget slot,
/// so the seeded run passes the same lineage rule production evidence does. Callers mutate the
/// returned entities (or pass explicit ids) to build the corrupt, foreign, or stale variants a
/// refusal test needs. Nothing is saved; the caller saves the context.
/// </summary>
internal sealed class PlanningLineageSeeder(
    DevalCopilotDbContext dbContext,
    Guid runId,
    Guid workspaceId,
    Guid checkpointId,
    string fingerprint,
    DateTimeOffset now,
    int nextAttemptNumber = 1)
{
    internal sealed record Review(Attempt Attempt, IReadOnlyList<CollaborationMessage> Outputs);

    internal sealed record Resolution(
        Attempt Attempt, IReadOnlyList<CollaborationMessage> Decisions, CollaborationMessage RevisedProposal);

    public int NextAttemptNumber => nextAttemptNumber;

    public (Attempt Planner, CollaborationMessage Proposal) AddRoot(Guid? proposalMessageId = null)
    {
        var number = nextAttemptNumber++;
        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, number);
        planner.MarkAgentDispatched(now);
        planner.CompleteAgent(AgentOutcome.Proposed, fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(planner);

        var proposal = CollaborationMessage.Record(
            proposalMessageId ?? Guid.NewGuid(), runId, planner.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Add the ledger table and its query.",
            ProposalJson("Ledger"), CollaborationMessageProvenance.ProviderObserved, now);
        dbContext.CollaborationMessages.Add(proposal);
        return (planner, proposal);
    }

    /// <summary>A completed critical review whose sole input is <paramref name="proposal"/>: an
    /// Acceptance for <see cref="AgentOutcome.Accepted"/>, otherwise <paramref name="challengeCount"/>
    /// ordered Challenges (with the given ids when supplied).</summary>
    public Review AddReview(
        CollaborationMessage proposal, AgentOutcome outcome, int challengeCount = 1, IReadOnlyList<Guid>? challengeIds = null)
    {
        var number = nextAttemptNumber++;
        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, number);
        review.MarkAgentDispatched(now);
        review.CompleteAgent(outcome, fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(review);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, proposal.Id, sequence: 0));

        var outputs = new List<CollaborationMessage>();
        if (outcome == AgentOutcome.Accepted)
        {
            outputs.Add(AddReviewMessage(
                review, Guid.NewGuid(), CollaborationMessageType.Acceptance, proposal.Id, "Accepted as proposed.",
                JsonSerializer.Serialize(new { rationale = "Sound and complete." })));
        }
        else
        {
            for (var index = 0; index < challengeCount; index++)
            {
                outputs.Add(AddReviewMessage(
                    review, challengeIds?[index] ?? Guid.NewGuid(), CollaborationMessageType.Challenge, proposal.Id,
                    $"Challenge {index + 1} summary",
                    JsonSerializer.Serialize(new
                    {
                        disputedItem = $"Disputed item {index + 1}",
                        materialImpact = $"Material impact {index + 1}",
                        reasoning = $"Reasoning {index + 1}",
                        alternativeOrQuestion = $"Alternative or question {index + 1}",
                    })));
            }
        }

        return new Review(review, outputs);
    }

    /// <summary>A completed Resolved resolver attempt for the given Challenged review: inputs are the
    /// reviewed Proposal then every Challenge in order, one Decision per Challenge, and one revised
    /// Proposal replying to the reviewed Proposal.</summary>
    public Resolution AddResolution(CollaborationMessage reviewedProposal, IReadOnlyList<CollaborationMessage> challenges)
    {
        var number = nextAttemptNumber++;
        var resolver = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, number);
        resolver.MarkAgentDispatched(now);
        resolver.CompleteAgent(AgentOutcome.Resolved, fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(resolver);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolver.Id, reviewedProposal.Id, sequence: 0));
        for (var index = 0; index < challenges.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(
                AttemptInputMessage.Record(Guid.NewGuid(), resolver.Id, challenges[index].Id, sequence: index + 1));
        }

        var decisions = challenges
            .Select((challenge, index) => AddResolverMessage(
                resolver, CollaborationMessageType.Decision, challenge.Id, $"Decision {index + 1} summary",
                JsonSerializer.Serialize(new
                {
                    resolution = "accepted",
                    rationale = $"Rationale {index + 1}",
                    resultingPlanChanges = $"Plan changes {index + 1}",
                    nextAction = "None",
                })))
            .ToArray();
        var revised = AddResolverMessage(
            resolver, CollaborationMessageType.Proposal, reviewedProposal.Id, "Revised ledger proposal.",
            ProposalJson($"Revised ledger scope {number}"));

        return new Resolution(resolver, decisions, revised);
    }

    /// <summary>Review (Challenged) then resolution of <paramref name="proposal"/> — one complete
    /// challenge-resolution round, yielding the next lineage level.</summary>
    public (Review Review, Resolution Resolution) AddChallengedRound(CollaborationMessage proposal, int challengeCount = 1)
    {
        var review = AddReview(proposal, AgentOutcome.Challenged, challengeCount);
        return (review, AddResolution(proposal, review.Outputs));
    }

    public CollaborationMessage AddReviewMessage(
        Attempt review, Guid id, CollaborationMessageType type, Guid inReplyTo, string summary, string structuredContentJson)
    {
        var message = CollaborationMessage.Record(
            id, runId, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            type, inReplyTo, summary, structuredContentJson, CollaborationMessageProvenance.ProviderObserved, now);
        dbContext.CollaborationMessages.Add(message);
        return message;
    }

    private CollaborationMessage AddResolverMessage(
        Attempt resolver, CollaborationMessageType type, Guid inReplyTo, string summary, string structuredContentJson)
    {
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), runId, resolver.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            type, inReplyTo, summary, structuredContentJson, CollaborationMessageProvenance.ProviderObserved, now);
        dbContext.CollaborationMessages.Add(message);
        return message;
    }

    public static string ProposalJson(string scope) => JsonSerializer.Serialize(new
    {
        scope,
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });
}
