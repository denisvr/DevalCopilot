using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;

/// <summary>
/// Bounded metadata for one historical Agent attempt: identity, lifecycle, host-measured process and
/// provider-reported token evidence, and metadata (never content) for the four allowlisted artifact
/// purposes. Never carries a storage path, content hash, provider session identifier, prompt,
/// adapter contract version, or artifact text. When <see cref="IdentityValid"/> is false the
/// persisted role, provider, contract, or assignment could not be proven coherent and only the
/// attempt's number and claim/completion times are disclosed (the status too, when it was readable).
/// </summary>
public sealed record GetAgentAttemptEvidenceQueryResult(
    bool IdentityValid,
    Guid AttemptId,
    int AttemptNumber,
    AttemptStatus? AttemptStatus,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    AgentProvider? Provider,
    AgentRole? Role,
    AgentResponseContract? ResponseContract,
    AgentOutcome? Outcome,
    DateTimeOffset? DispatchedAtUtc,
    TimeSpan? Timeout,
    AgentProcessExecutionEvidence? ProcessExecution,
    AgentTokenUsageEvidence? TokenUsage,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts);
