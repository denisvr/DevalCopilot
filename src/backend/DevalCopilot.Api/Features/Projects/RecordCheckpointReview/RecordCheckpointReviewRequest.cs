namespace DevalCopilot.Api.Features.Projects.RecordCheckpointReview;

/// <param name="VerificationExecutionId">The legacy single-execution evidence form.</param>
/// <param name="VerificationExecutionIds">The optional Human execution-set form (1 to 32 unique, nonempty identifiers, or empty only
/// for Pending). Supplying both forms is refused.</param>
public sealed record RecordCheckpointReviewRequest(
    Guid GitCheckpointId,
    Guid? VerificationExecutionId,
    string ActorKind,
    string Decision,
    IReadOnlyList<Guid>? VerificationExecutionIds = null);
