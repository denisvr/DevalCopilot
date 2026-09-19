using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;

public sealed record CheckpointReviewQueryResult(
    Guid ReviewId,
    Guid GitCheckpointId,
    int CheckpointNumber,
    string CheckpointFingerprintSha256,
    IReadOnlyList<CheckpointReviewEvidenceQueryResult> Evidence,
    ReviewActorKind ActorKind,
    ReviewDecision Decision,
    bool IsApplicable,
    string? StaleReasonCode,
    DateTimeOffset RecordedAtUtc);

public sealed record CheckpointReviewEvidenceQueryResult(
    Guid VerificationCommandId,
    Guid VerificationExecutionId,
    int VerificationExecutionNumber,
    VerificationExecutionStatus VerificationExecutionStatus,
    VerificationExecutionOutcome? VerificationExecutionOutcome);
