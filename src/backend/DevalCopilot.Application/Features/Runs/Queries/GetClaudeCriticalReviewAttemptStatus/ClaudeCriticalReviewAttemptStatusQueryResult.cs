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
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Claude CriticalReviewer CLI permission mode this attempt's fixed adapter
    /// contract configures — never provider-observed effective behavior, mode availability, or
    /// invocation eligibility. Populated only when provider, role, permission profile, and
    /// adapter contract version all agree with the current, single supported critical-review
    /// adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredPermissionMode = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// provider-session persistence as disabled (the current adapter's own
    /// <c>--no-session-persistence</c> argument) — a static configuration fact, never a
    /// provider-observed result. Populated only when provider, role, permission profile, and
    /// adapter contract version all agree with the current, single supported critical-review
    /// adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredSessionPersistence = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// interactive permission-confirmation prompts as denied in print mode (the current adapter's
    /// own <c>--permission-prompts none</c> argument) — a static configuration fact, never a
    /// provider-observed result and never invocation eligibility. Populated only when provider,
    /// role, permission profile, and adapter contract version all agree with the current, single
    /// supported critical-review adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredPermissionPrompts = null,
    /// <summary>Whether this attempt's fixed adapter contract makes its Claude CLI provider
    /// session ineligible for resume (the current adapter's own <c>--no-session-persistence</c>
    /// argument) — a static configuration fact, never a provider-observed result, never a
    /// host-wide capability assessment, and never invocation eligibility. Populated only when
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported critical-review adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredResumeEligibility = null,
    /// <summary>The Claude CriticalReviewer CLI built-in tool allowlist this attempt's fixed
    /// adapter contract configures (the current adapter's own explicit empty <c>--tools</c>
    /// argument, shown as <c>"None"</c>) — a static configuration fact, never an observation of
    /// effective access, a complete security boundary, MCP tool restriction, or invocation
    /// eligibility. Populated only when provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported critical-review adapter; otherwise
    /// <see langword="null"/>.</summary>
    string? ConfiguredBuiltInTools = null)
{
    public static readonly ClaudeCriticalReviewAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, []);
}
