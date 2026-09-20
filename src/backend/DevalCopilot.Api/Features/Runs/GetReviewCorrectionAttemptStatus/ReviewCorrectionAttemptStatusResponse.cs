using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

namespace DevalCopilot.Api.Features.Runs.GetReviewCorrectionAttemptStatus;

public sealed record ReviewCorrectionAttemptStatusResponse(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ImplementationReviewAttemptId,
    string? Status,
    string? Outcome,
    Guid? StartingGitCheckpointId,
    Guid? ResultGitCheckpointId,
    int RevisionResponseCount,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentAttemptArtifactMetadataResponse> Artifacts);
