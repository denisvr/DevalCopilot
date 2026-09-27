using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetReviewCorrectionAttemptStatus;

public sealed record ReviewCorrectionAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ImplementationReviewAttemptId,
    Guid? ReviewableExecutionReportMessageId,
    string? Status,
    string? Outcome,
    Guid? StartingGitCheckpointId,
    Guid? ResultGitCheckpointId,
    int RevisionResponseCount,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    int MaximumReviewCorrectionAttempts,
    int ReviewCorrectionAttemptsUsed,
    bool BudgetExhausted,
    Guid? EscalationId,
    Guid? EscalationMessageId,
    bool HasAvailableHumanAuthorization,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    /// <summary>The Claude Implementer CLI permission mode this attempt's fixed adapter contract
    /// configures for review correction — a static, read-only configuration fact, never
    /// provider-observed effective behavior, mode availability, or invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported review-correction adapter.</summary>
    string? ConfiguredPermissionMode,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// provider-session persistence as <c>"Disabled"</c> — a static configuration fact, never a
    /// provider-observed result. <see langword="null"/> unless provider, role, permission
    /// profile, and adapter contract version all agree with the current, single supported
    /// review-correction adapter.</summary>
    string? ConfiguredSessionPersistence,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// interactive permission-confirmation prompts as denied in print mode — a static
    /// configuration fact, never a provider-observed result and never invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported review-correction adapter.</summary>
    string? ConfiguredPermissionPrompts,
    /// <summary>Whether this attempt's fixed adapter contract makes its Claude CLI provider
    /// session ineligible for resume — a static configuration fact, never a provider-observed
    /// result, never a host-wide capability assessment, and never invocation eligibility.
    /// <see langword="null"/> unless provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported review-correction adapter.</summary>
    string? ConfiguredResumeEligibility,
    /// <summary>The Claude Implementer CLI built-in tool allowlist this attempt's fixed adapter
    /// contract configures for review correction — a static configuration fact, never an
    /// observation of effective access, a complete security boundary, MCP tool restriction, or
    /// invocation eligibility. <see langword="null"/> unless provider, role, permission profile,
    /// and adapter contract version all agree with the current, single supported
    /// review-correction adapter.</summary>
    string? ConfiguredBuiltInTools);
