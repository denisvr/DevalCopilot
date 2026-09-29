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
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Codex Planner CLI command sandbox this attempt's fixed adapter contract
    /// configures (the shared <c>CodexProcessInvoker</c>'s own <c>--sandbox read-only</c>
    /// argument) — a static configuration fact, never a provider-observed effective isolation
    /// result, a complete access-control boundary, or invocation eligibility. Populated only when
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported planning adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredCommandSandbox = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Codex CLI's session
    /// rollout-file persistence as disabled (the shared <c>CodexProcessInvoker</c>'s own
    /// <c>--ephemeral</c> argument) — a static configuration fact, never a provider-observed
    /// result, resume eligibility, or invocation eligibility. Populated only when provider, role,
    /// permission profile, and adapter contract version all agree with the current, single
    /// supported planning adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredRolloutPersistence = null,
    /// <summary>Immutable lineage: the Planner attempt this attempt is the one manual format repair
    /// of, or <see langword="null"/> for an ordinary attempt. Always present for a repair, even when no source
    /// attempt of this run is found. Provenance only — never a claim that this attempt corrected the source.</summary>
    Guid? RepairSourceAttemptId = null,
    /// <summary>The source attempt's number within this run, or <see langword="null"/> for an ordinary attempt or
    /// when no source attempt of the same run is found (the id above stays present). A source with an
    /// invalid persisted enum does not by itself make this scalar projection null.</summary>
    int? RepairSourceAttemptNumber = null)
{
    public static readonly AgentAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, []);
}

/// <summary>Metadata only — never a storage path or content hash, both of which stay internal to
/// the artifact store and its own protected readers. <c>Truncated</c> is <see langword="null"/>
/// exactly when it is genuinely unknown (an artifact recovered from a host interruption).</summary>
public sealed record AgentAttemptArtifactMetadata(
    ArtifactPurpose Purpose, long ByteLength, bool? Truncated, ArtifactCaptureOutcome CaptureOutcome);
