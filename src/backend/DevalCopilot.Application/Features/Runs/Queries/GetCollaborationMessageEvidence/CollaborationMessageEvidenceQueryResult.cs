using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

/// <summary>The three mutually exclusive shapes this query can truthfully return for an existing,
/// resolved collaboration message. Never inferred from field nullability alone — always read this
/// discriminator first.</summary>
public enum CollaborationMessageEvidenceStatus
{
    /// <summary>The message is <c>ProviderObserved</c>, its <c>AttemptId</c> resolved to a real,
    /// currently persisted Agent-kind <c>Attempt</c> row, and that attempt's own role and provider
    /// match the message's actor — every evidence field below is populated for that exact,
    /// coherently-owned attempt.</summary>
    HasEvidence,

    /// <summary>The message's <c>Provenance</c> is not <c>ProviderObserved</c> — Human,
    /// Orchestrator, or Simulated — so it legitimately has no Agent evidence, independent of
    /// whether it happens to carry a non-null <c>AttemptId</c> of its own (a Simulated message
    /// links to a Simulated attempt, not an Agent one). Not an error, and not the same as evidence
    /// being unavailable.</summary>
    NoAgentEvidence,

    /// <summary>The message is <c>ProviderObserved</c> but its evidence could not be trusted:
    /// <c>AttemptId</c> is null, no matching <c>Attempt</c> row exists, the matching row is not an
    /// Agent-kind attempt, or its role/provider does not match the message's own actor. Several of
    /// these should be impossible under the durable foreign-key constraint and the invariants
    /// <c>CollaborationMessage.RecordAgent</c> enforces at construction; this status exists so the
    /// read side fails closed — never substituting or fabricating evidence — if any of them is ever
    /// observed.</summary>
    AttemptLinkBroken,
}

/// <summary>
/// Bounded evidence about the exact attempt that produced one collaboration message. Every
/// checkpoint identity below is historical — the attempt's own starting/result checkpoint at the
/// time it ran — and is never evidence about the run's current source state. Never carries a
/// process executable path, argument, working directory, approved root, provider session
/// identifier, adapter contract version, or any raw stdout/stderr/response/prompt content; see
/// <see cref="AgentAttemptArtifactMetadata"/> for the same exclusions applied to artifact rows.
/// </summary>
public sealed record CollaborationMessageEvidenceQueryResult(
    CollaborationMessageEvidenceStatus Status,
    Guid? AttemptId,
    int? AttemptNumber,
    AttemptKind? AttemptKind,
    AttemptStatus? AttemptStatus,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    AgentProvider? AgentProvider,
    AgentRole? AgentRole,
    AgentResponseContract? AgentResponseContract,
    AgentOutcome? AgentOutcome,
    DateTimeOffset? AgentDispatchedAtUtc,
    /// <summary>This attempt's own immutable STARTING checkpoint at claim time — historical, never
    /// evidence about the run's current source state.</summary>
    Guid? StartingGitCheckpointId,
    /// <summary>The starting checkpoint's fingerprint at claim time — historical.</summary>
    string? StartingCheckpointFingerprintSha256,
    /// <summary>The checkpoint this attempt's own real, verified source mutation produced, if any —
    /// historical, and only ever set for a successful <c>Implemented</c>/review-correction
    /// outcome.</summary>
    Guid? ResultGitCheckpointId,
    /// <summary>The result checkpoint's own persisted fingerprint — historical — populated only
    /// when <see cref="ResultGitCheckpointId"/> resolves to a real <c>GitCheckpoint</c> row whose
    /// own <c>WorkspaceId</c> matches this attempt's <c>AgentGitWorkspaceId</c>. A missing row or a
    /// workspace mismatch is an incoherent reference: it fails safely by leaving this field null
    /// rather than substituting a checkpoint from a different workspace.</summary>
    string? ResultCheckpointFingerprintSha256,
    /// <summary>This attempt's own configured provider-invocation timeout — an existing Agent
    /// attempt always carries one, independent of whether process-execution evidence exists.</summary>
    TimeSpan? AgentTimeout,
    AgentProcessExecutionEvidence? ProcessExecution,
    AgentTokenUsageEvidence? TokenUsage,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    /// <summary>True when this attempt has more artifacts than the bounded cap returned in
    /// <see cref="Artifacts"/> — the omission is always stated, never silent.</summary>
    bool ArtifactsOmitted,
    /// <summary>The attempt's real total artifact count, independent of how many are returned in
    /// <see cref="Artifacts"/>.</summary>
    int ArtifactTotalCount)
{
    public static readonly CollaborationMessageEvidenceQueryResult NoAgentEvidence = new(
        CollaborationMessageEvidenceStatus.NoAgentEvidence,
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, [], false, 0);

    public static readonly CollaborationMessageEvidenceQueryResult AttemptLinkBroken = new(
        CollaborationMessageEvidenceStatus.AttemptLinkBroken,
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, [], false, 0);
}
