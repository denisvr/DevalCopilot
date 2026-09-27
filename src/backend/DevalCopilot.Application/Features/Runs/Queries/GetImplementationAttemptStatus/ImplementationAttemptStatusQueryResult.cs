using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;

/// <summary>Mirrors <c>ChallengeResolutionAttemptStatusQueryResult</c>'s discriminator pattern,
/// plus the starting and resulting checkpoint identities and the bounded changed-file evidence a
/// successful implementation actually produced — never an absolute path, prompt, manifest,
/// credential, environment value, or raw transcript. <see cref="HasAttempt"/> is the explicit
/// discriminator for "this run has never requested an implementation" — every other field is
/// <see langword="null"/>/empty in that case, and the result is still a real, non-null success
/// value.</summary>
public sealed record ImplementationAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    /// <summary>The implemented plan's sequence-0 Proposal message id — lets a caller determine
    /// whether this status still belongs to the currently eligible plan, or is stale evidence
    /// from an older, since-superseded one. Mirrors
    /// <c>ChallengeResolutionAttemptStatusQueryResult.OriginalProposalMessageId</c>'s own
    /// purpose exactly.</summary>
    Guid? PlanProposalMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    Guid? StartingGitCheckpointId,
    string? StartingCheckpointFingerprintSha256,
    Guid? ResultGitCheckpointId,
    string? ResultCheckpointFingerprintSha256,
    string? ExecutionReportSummary,
    IReadOnlyList<string> ChangedRelativePaths,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentAssignmentSnapshot? Assignment,
    AgentRole? Role,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Claude Implementer CLI permission mode this attempt's fixed adapter contract
    /// configures — never provider-observed effective behavior, mode availability, or invocation
    /// eligibility. Populated only when provider, role, permission profile, and adapter contract
    /// version all agree with the current, single supported implementation adapter; otherwise
    /// <see langword="null"/>, exactly like every other assignment fact here.</summary>
    string? ConfiguredPermissionMode = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// provider-session persistence as disabled (the current adapter's own
    /// <c>--no-session-persistence</c> argument) — a static configuration fact, never a
    /// provider-observed result and never DevalCopilot's own durable attempt history, which is
    /// unaffected by this flag either way. Populated only when provider, role, permission
    /// profile, and adapter contract version all agree with the current, single supported
    /// implementation adapter; otherwise <see langword="null"/>, exactly like every other
    /// assignment fact here.</summary>
    string? ConfiguredSessionPersistence = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Claude CLI's
    /// interactive permission-confirmation prompts as denied in print mode (the current adapter's
    /// own <c>--permission-prompts none</c> argument) — a static configuration fact, never a
    /// provider-observed result and never invocation eligibility. Populated only when provider,
    /// role, permission profile, and adapter contract version all agree with the current, single
    /// supported implementation adapter; otherwise <see langword="null"/>, exactly like every
    /// other assignment fact here.</summary>
    string? ConfiguredPermissionPrompts = null,
    /// <summary>Whether this attempt's fixed adapter contract makes its Claude CLI provider
    /// session ineligible for resume (the current adapter's own <c>--no-session-persistence</c>
    /// argument, which the Claude Code CLI reference documents as preventing a session started
    /// under it from being resumed) — a static configuration fact about this attempt's adapter
    /// contract, never a provider-observed result, never a host-wide capability assessment, and
    /// never invocation eligibility. Populated only when provider, role, permission profile, and
    /// adapter contract version all agree with the current, single supported implementation
    /// adapter; otherwise <see langword="null"/>, exactly like every other assignment fact
    /// here.</summary>
    string? ConfiguredResumeEligibility = null)
{
    public static readonly ImplementationAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, null, null, [], null, null, null, [], null, null);
}
