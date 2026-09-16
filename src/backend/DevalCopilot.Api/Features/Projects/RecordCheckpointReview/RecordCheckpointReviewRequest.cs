namespace DevalCopilot.Api.Features.Projects.RecordCheckpointReview;

public sealed record RecordCheckpointReviewRequest(
    Guid GitCheckpointId,
    Guid? VerificationExecutionId,
    string ActorKind,
    string Decision);
