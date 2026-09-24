using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusQueryResult</c> exactly, plus the exact
/// reviewed ExecutionReport message identity every code-review attempt carries.
/// <see cref="HasAttempt"/> is the explicit discriminator for "this run has never requested a code
/// review" — every other field is <see langword="null"/>/empty in that case, and the result is
/// still a real, non-null success value. Never exposes an executable path, argument, prompt, raw
/// manifest, credential, environment value, full provider output, or local absolute path.</summary>
public sealed record CodeReviewAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ExecutionReportMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null)
{
    public static readonly CodeReviewAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, []);
}
