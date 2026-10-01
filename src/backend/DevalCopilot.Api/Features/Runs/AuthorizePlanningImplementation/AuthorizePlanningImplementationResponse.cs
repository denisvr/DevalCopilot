namespace DevalCopilot.Api.Features.Runs.AuthorizePlanningImplementation;

public sealed record AuthorizePlanningImplementationResponse(
    string Status,
    Guid AuthorizationId,
    Guid EscalationMessageId,
    Guid FinalProposalMessageId,
    Guid HumanInstructionMessageId,
    long? LatestEventSequence);
