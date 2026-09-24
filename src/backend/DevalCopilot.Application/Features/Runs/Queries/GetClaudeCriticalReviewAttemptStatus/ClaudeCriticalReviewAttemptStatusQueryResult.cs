using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;

/// <summary>Mirrors <c>AgentAttemptStatusQueryResult</c> exactly, plus the exact reviewed
/// Proposal message identity every critical-review attempt carries. <see cref="HasAttempt"/> is
/// the explicit discriminator for "this run has never requested a Claude critical review" —
/// every other field is <see langword="null"/>/empty in that case, and the result is still a
/// real, non-null success value.</summary>
public sealed record ClaudeCriticalReviewAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ReviewedProposalMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null)
{
    public static readonly ClaudeCriticalReviewAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, []);
}
