namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

/// <summary>Always a real body for an existing run. <c>HasAttempt: false</c> means this run has
/// never requested a Codex plan — every other field is then null/empty, not merely absent.</summary>
public sealed record AgentAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    /// <summary>The Codex Planner CLI command sandbox this attempt's fixed adapter contract
    /// configures — a static, read-only configuration fact, never provider-observed effective
    /// isolation, a complete access-control boundary, or invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported planning adapter.</summary>
    string? ConfiguredCommandSandbox,
    /// <summary>Whether this attempt's fixed adapter contract configures the Codex CLI's session
    /// rollout-file persistence as <c>"Disabled"</c> — a static configuration fact, never a
    /// provider-observed result, resume eligibility, or invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported planning adapter.</summary>
    string? ConfiguredRolloutPersistence);
