using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetChallengeResolutionAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusQueryResult</c> exactly, plus the exact
/// original Proposal and ordered Challenge message identities every challenge-resolution attempt
/// resolves. <see cref="HasAttempt"/> is the explicit discriminator for "this run has never
/// requested a challenge resolution" — every other field is <see langword="null"/>/empty in that
/// case, and the result is still a real, non-null success value.</summary>
public sealed record ChallengeResolutionAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? OriginalProposalMessageId,
    IReadOnlyList<Guid> ChallengeMessageIds,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts,
    AgentProcessExecutionEvidence? ProcessExecution = null,
    TimeSpan? Timeout = null,
    AgentTokenUsageEvidence? TokenUsage = null,
    /// <summary>The Codex Resolver CLI command sandbox this attempt's fixed adapter contract
    /// configures (the shared <c>CodexProcessInvoker</c>'s own <c>--sandbox read-only</c>
    /// argument) — a static configuration fact, never a provider-observed effective isolation
    /// result, a complete access-control boundary, or invocation eligibility. Populated only when
    /// provider, role, permission profile, and adapter contract version all agree with the
    /// current, single supported challenge-resolution adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredCommandSandbox = null,
    /// <summary>Whether this attempt's fixed adapter contract configures the Codex CLI's session
    /// rollout-file persistence as disabled (the shared <c>CodexProcessInvoker</c>'s own
    /// <c>--ephemeral</c> argument) — a static configuration fact, never a provider-observed
    /// result, resume eligibility, or invocation eligibility. Populated only when provider, role,
    /// permission profile, and adapter contract version all agree with the current, single
    /// supported challenge-resolution adapter; otherwise <see langword="null"/>.</summary>
    string? ConfiguredRolloutPersistence = null,
    /// <summary>Immutable lineage: the attempt this attempt is the one manual format repair of, or
    /// <see langword="null"/> for an ordinary attempt. Always present for a repair, even when no source attempt of
    /// this run is found. Provenance only — never a claim that this attempt corrected the source.</summary>
    Guid? RepairSourceAttemptId = null,
    /// <summary>The source attempt's number within this run, or <see langword="null"/> for an ordinary attempt
    /// or when no source attempt of the same run is found (the id above stays present).</summary>
    int? RepairSourceAttemptNumber = null)
{
    public static readonly ChallengeResolutionAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, [], null, null, null, null, null, []);
}
