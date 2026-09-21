namespace DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrection;

public sealed record AuthorizeReviewCorrectionResponse(
    string Status,
    Guid EscalationId,
    Guid HumanInstructionMessageId,
    Guid AuthorizationId,
    long? LatestEventSequence);
