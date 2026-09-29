using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>
/// Bounded metadata for one historical Agent attempt selected from the run history. When
/// <c>IdentityValid</c> is false the persisted role, provider, contract, or assignment could not be
/// proven coherent: only the number, status, and claim/completion times are present and every other
/// field is null or empty. Artifact entries describe only the four allowlisted Agent purposes and
/// never carry a storage path, content hash, or text. Never carries a provider session identifier,
/// prompt, adapter contract version, executable path, argument, or working directory.
/// </summary>
public sealed record AgentAttemptEvidenceResponse(
    bool IdentityValid,
    Guid AttemptId,
    int AttemptNumber,
    string? AttemptStatus,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Provider,
    string? Role,
    string? ResponseContract,
    string? Outcome,
    DateTimeOffset? DispatchedAtUtc,
    AgentProcessExecutionResponse? ProcessExecution,
    AgentTokenUsageResponse? TokenUsage,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts);
