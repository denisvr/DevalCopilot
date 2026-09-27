using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetChallengeResolutionAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusResponse</c> exactly, plus the original
/// Proposal and ordered Challenge message identities. <c>HasAttempt: false</c> means this run has
/// never requested a challenge resolution — every other field is then null/empty, not merely
/// absent.</summary>
public sealed record ChallengeResolutionAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? OriginalProposalMessageId,
    IReadOnlyList<Guid> ChallengeMessageIds,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    /// <summary>The Codex Resolver CLI command sandbox this attempt's fixed adapter contract
    /// configures — a static, read-only configuration fact, never provider-observed effective
    /// isolation, a complete access-control boundary, or invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported challenge-resolution adapter.</summary>
    string? ConfiguredCommandSandbox,
    /// <summary>Whether this attempt's fixed adapter contract configures the Codex CLI's session
    /// rollout-file persistence as <c>"Disabled"</c> — a static configuration fact, never a
    /// provider-observed result, resume eligibility, or invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported challenge-resolution adapter.</summary>
    string? ConfiguredRolloutPersistence);
