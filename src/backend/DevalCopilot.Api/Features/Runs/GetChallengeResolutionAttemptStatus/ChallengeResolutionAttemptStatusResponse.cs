using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetChallengeResolutionAttemptStatus;

/// <summary>Mirrors <c>ClaudeCriticalReviewAttemptStatusResponse</c> exactly, plus the original
/// Proposal and ordered Challenge message identities. <c>HasAttempt: false</c> means this run has
/// never requested a challenge resolution — every other field is then null/empty, not merely
/// absent.</summary>
public sealed record ChallengeResolutionAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? OriginalProposalMessageId,
    IReadOnlyList<Guid> ChallengeMessageIds,
    string? Status,
    string? Outcome,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts);
