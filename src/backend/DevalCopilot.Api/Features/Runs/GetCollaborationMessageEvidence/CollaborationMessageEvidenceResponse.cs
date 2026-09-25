using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationMessageEvidence;

/// <summary>
/// Bounded, read-only evidence about the exact Agent attempt that produced one collaboration
/// message. <c>EvidenceStatus</c> is <c>HasEvidence</c>, <c>NoAgentEvidence</c> (a legitimate
/// Human/Orchestrator/Simulated message — every other field is then null/empty), or
/// <c>AttemptLinkBroken</c> (the message's attempt link could not be resolved — every other field
/// is then null/empty; this should be impossible under the durable foreign key, but is never
/// coalesced with <c>NoAgentEvidence</c> or silently substituted). Every checkpoint identity here
/// is HISTORICAL — this attempt's own starting/result checkpoint at the time it ran — and is never
/// evidence about the run's current source state. Never carries a process executable path,
/// argument, working directory, approved root, provider session identifier, adapter contract
/// version, or any raw stdout/stderr/response/prompt/context-manifest content.
/// </summary>
public sealed record CollaborationMessageEvidenceResponse(
    string EvidenceStatus,
    Guid? AttemptId,
    int? AttemptNumber,
    string? AttemptKind,
    string? AttemptStatus,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? AgentProvider,
    string? AgentRole,
    string? AgentResponseContract,
    string? AgentOutcome,
    DateTimeOffset? AgentDispatchedAtUtc,
    Guid? StartingGitCheckpointId,
    string? StartingCheckpointFingerprintSha256,
    Guid? ResultGitCheckpointId,
    string? ResultCheckpointFingerprintSha256,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts,
    bool ArtifactsOmitted,
    int ArtifactTotalCount);
