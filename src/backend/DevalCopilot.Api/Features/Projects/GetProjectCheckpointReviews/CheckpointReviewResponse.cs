namespace DevalCopilot.Api.Features.Projects.GetProjectCheckpointReviews;

public sealed record CheckpointReviewResponse(
    Guid ReviewId,
    Guid GitCheckpointId,
    int CheckpointNumber,
    string CheckpointFingerprintSha256,
    IReadOnlyList<CheckpointReviewEvidenceResponse> Evidence,
    string ActorKind,
    string Decision,
    bool IsApplicable,
    string? StaleReasonCode,
    DateTimeOffset RecordedAtUtc);

public sealed record CheckpointReviewEvidenceResponse(
    Guid VerificationCommandId,
    Guid VerificationExecutionId,
    int VerificationExecutionNumber,
    string VerificationExecutionStatus,
    string? VerificationExecutionOutcome);
