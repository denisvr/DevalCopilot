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
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Codex CodeReviewer CLI command sandbox this attempt's fixed adapter contract
    /// configures (the shared <c>CodexProcessInvoker</c>'s own <c>--sandbox read-only</c>
    /// argument) — a static configuration fact, never a provider-observed effective isolation
    /// result, a complete access-control boundary, or invocation eligibility. Populated only when
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported code-review adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredCommandSandbox = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Codex CLI's session
    /// rollout-file persistence as disabled (the shared <c>CodexProcessInvoker</c>'s own
    /// <c>--ephemeral</c> argument) — a static configuration fact, never a provider-observed
    /// result, resume eligibility, or invocation eligibility. Populated only when provider, role,
    /// permission profile, and adapter contract version all agree with the current, single
    /// supported code-review adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredRolloutPersistence = null)
{
    public static readonly CodeReviewAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, []);
}
