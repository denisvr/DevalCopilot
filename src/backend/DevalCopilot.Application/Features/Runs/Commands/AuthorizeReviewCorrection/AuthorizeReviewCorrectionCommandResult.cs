namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;

public sealed record AuthorizeReviewCorrectionCommandResult(
    Guid EscalationId,
    Guid HumanInstructionMessageId,
    Guid AuthorizationId,
    string Status,
    long? LatestEventSequence);
