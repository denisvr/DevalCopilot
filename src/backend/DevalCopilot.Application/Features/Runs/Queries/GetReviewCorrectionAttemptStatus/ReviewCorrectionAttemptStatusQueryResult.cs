using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;

public sealed record ReviewCorrectionAttemptStatusQueryResult(
    bool HasAttempt,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? ImplementationReviewAttemptId,
    Guid? ReviewableExecutionReportMessageId,
    AttemptStatus? Status,
    AgentOutcome? Outcome,
    Guid? StartingGitCheckpointId,
    Guid? ResultGitCheckpointId,
    int RevisionResponseCount,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<AgentCorrectionArtifactMetadata> Artifacts)
{
    public static readonly ReviewCorrectionAttemptStatusQueryResult NoAttempt =
        new(false, null, null, null, null, null, null, null, null, 0, null, null, null, []);
}

public sealed record AgentCorrectionArtifactMetadata(
    ArtifactPurpose Purpose, long ByteLength, bool? Truncated, ArtifactCaptureOutcome CaptureOutcome);
