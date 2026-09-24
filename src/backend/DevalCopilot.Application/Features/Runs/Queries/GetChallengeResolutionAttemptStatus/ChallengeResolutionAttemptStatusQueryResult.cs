using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetChallengeResolutionAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusQueryResult</c> exactly, plus the exact
/// original Proposal and ordered Challenge message identities every challenge-resolution attempt
/// resolves. <see cref="HasAttempt"/> is the explicit discriminator for "this run has never
/// requested a challenge resolution" — every other field is <see langword="null"/>/empty in that
/// case, and the result is still a real, non-null success value.</summary>
public sealed record ChallengeResolutionAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? OriginalProposalMessageId,
    IReadOnlyList<Guid> ChallengeMessageIds,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null)
{
    public static readonly ChallengeResolutionAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, [], null, null, null, null, null, []);
}
