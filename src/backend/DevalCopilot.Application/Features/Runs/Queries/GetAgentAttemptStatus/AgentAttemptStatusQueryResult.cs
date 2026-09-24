using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;

/// <summary>
/// <see cref="HasAttempt"/> is the explicit discriminator for "this run exists but has never
/// requested a Codex plan" — every other field is <see langword="null"/>/empty in that case, and
/// the result is still a real, non-null success value (never <c>Result&lt;T&gt;.Success(null)</c>
/// and never an ambiguous absent body at the API boundary).
/// </summary>
public sealed record AgentAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null)
{
    public static readonly AgentAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, []);
}

/// <summary>Metadata only — never a storage path or content hash, both of which stay internal to
/// the artifact store and its own protected readers. <c>Truncated</c> is <see langword="null"/>
/// exactly when it is genuinely unknown (an artifact recovered from a host interruption).</summary>
public sealed record AgentAttemptArtifactMetadata(
    ArtifactPurpose Purpose, long ByteLength, bool? Truncated, ArtifactCaptureOutcome CaptureOutcome);
