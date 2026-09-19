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
    IReadOnlyList<AgentAttemptArtifactMetadata> Artifacts)
{
    public static readonly ImplementationAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, null, null, [], null, null, null, []);
}
