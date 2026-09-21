namespace DevalCopilot.Api.Features.Runs.RequestReviewCorrection;

public sealed record RequestReviewCorrectionResponse(
    string Status,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? EscalationId,
    Guid? EscalationMessageId,
    long? LatestEventSequence);
