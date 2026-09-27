using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetImplementationAttemptStatus;

/// <summary>Mirrors <c>ChallengeResolutionAttemptStatusResponse</c>'s discriminator pattern, plus
/// the starting/resulting checkpoint identities and the bounded changed-file evidence a
/// successful implementation actually produced. <c>hasAttempt: false</c> means this run has never
/// requested an implementation — every other field is then null/empty, not merely absent. Never
/// an absolute path, prompt, manifest, credential, environment value, or raw transcript.</summary>
public sealed record ImplementationAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? PlanProposalMessageId,
    string? Status,
    string? Outcome,
    Guid? StartingGitCheckpointId,
    string? StartingCheckpointFingerprintSha256,
    Guid? ResultGitCheckpointId,
    string? ResultCheckpointFingerprintSha256,
    string? ExecutionReportSummary,
    IReadOnlyList<string> ChangedRelativePaths,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    string? Provider,
    string? Role,
    string? RequestedModel,
    string? ObservedModel,
    string? RequestedEffort,
    string? ObservedEffort,
    string? PermissionProfile,
    string? AdapterContractVersion,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    /// <summary>The Claude Implementer CLI permission mode this attempt's fixed adapter contract
    /// configures — a static, read-only configuration fact, never provider-observed effective
    /// behavior, mode availability, or invocation eligibility. <see langword="null"/> unless
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported implementation adapter.</summary>
    string? ConfiguredPermissionMode,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// provider-session persistence as <c>"Disabled"</c> (the current adapter's own
    /// <c>--no-session-persistence</c> argument) — a static configuration fact, never a
    /// provider-observed result and never DevalCopilot's own durable attempt history, which this
    /// flag does not affect either way. <see langword="null"/> unless provider, role, permission
    /// profile, and adapter contract version all agree with the current, single supported
    /// implementation adapter.</summary>
    string? ConfiguredSessionPersistence,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// interactive permission-confirmation prompts as denied in print mode (the current adapter's
    /// own <c>--permission-prompts none</c> argument) — a static configuration fact, never a
    /// provider-observed result and never invocation eligibility. <see langword="null"/> unless
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported implementation adapter.</summary>
    string? ConfiguredPermissionPrompts,
    /// <summary>Whether this attempt's fixed adapter contract makes its Claude CLI provider
    /// session ineligible for resume (the current adapter's own <c>--no-session-persistence</c>
    /// argument, which the Claude Code CLI reference documents as preventing a session started
    /// under it from being resumed) — a static configuration fact about this attempt's adapter
    /// contract, never a provider-observed result, never a host-wide capability assessment, and
    /// never invocation eligibility. <see langword="null"/> unless provider, role, permission
    /// profile, and adapter contract version all agree with the current, single supported
    /// implementation adapter.</summary>
    string? ConfiguredResumeEligibility,
    /// <summary>The Claude Implementer CLI built-in tool allowlist this attempt's fixed adapter
    /// contract configures (the current adapter's own <c>--tools</c> argument) — a static
    /// configuration fact, never an observation of effective access, a complete security boundary,
    /// MCP tool restriction, or invocation eligibility. <see langword="null"/> unless provider,
    /// role, permission profile, and adapter contract version all agree with the current, single
    /// supported implementation adapter.</summary>
    string? ConfiguredBuiltInTools);
