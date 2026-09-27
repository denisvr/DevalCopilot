using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;

public sealed record ReviewCorrectionAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ImplementationReviewAttemptId,
    Guid? ReviewableExecutionReportMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    Guid? StartingGitCheckpointId,
    Guid? ResultGitCheckpointId,
    int RevisionResponseCount,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentCorrectionArtifactMetadata> Artifacts,
    int MaximumReviewCorrectionAttempts,
    int ReviewCorrectionAttemptsUsed,
    bool BudgetExhausted,
    Guid? EscalationId,
    Guid? EscalationMessageId,
    bool HasAvailableHumanAuthorization,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Claude Implementer CLI permission mode this attempt's fixed adapter contract
    /// configures for review correction — never provider-observed effective behavior, mode
    /// availability, or invocation eligibility. Populated only when provider, role, permission
    /// profile, and adapter contract version all agree with the current, single supported
    /// review-correction adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredPermissionMode = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// provider-session persistence as disabled — a static configuration fact, never a
    /// provider-observed result. Populated only when provider, role, permission profile, and
    /// adapter contract version all agree with the current, single supported review-correction
    /// adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredSessionPersistence = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// interactive permission-confirmation prompts as denied in print mode — a static
    /// configuration fact, never a provider-observed result and never invocation eligibility.
    /// Populated only when provider, role, permission profile, and adapter contract version all
    /// agree with the current, single supported review-correction adapter; otherwise
    /// <see langword="null"/>.</summary>
    string? ConfiguredPermissionPrompts = null,
    /// <summary>Whether this attempt's fixed adapter contract makes its Claude CLI provider
    /// session ineligible for resume — a static configuration fact, never a provider-observed
    /// result, never a host-wide capability assessment, and never invocation eligibility.
    /// Populated only when provider, role, permission profile, and adapter contract version all
    /// agree with the current, single supported review-correction adapter; otherwise
    /// <see langword="null"/>.</summary>
    string? ConfiguredResumeEligibility = null,
    /// <summary>The Claude Implementer CLI built-in tool allowlist this attempt's fixed adapter
    /// contract configures for review correction — a static configuration fact, never an
    /// observation of effective access, a complete security boundary, MCP tool restriction, or
    /// invocation eligibility. Populated only when provider, role, permission profile, and adapter
    /// contract version all agree with the current, single supported review-correction adapter;
    /// otherwise <see langword="null"/>.</summary>
    string? ConfiguredBuiltInTools = null)
{
    public static readonly ReviewCorrectionAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, 0, null, null, null, [], 2, 0, false, null, null, false);
}

public sealed record AgentCorrectionArtifactMetadata(
    ArtifactPurpose Purpose, long ByteLength, bool? Truncated, ArtifactCaptureOutcome CaptureOutcome);
