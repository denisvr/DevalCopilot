namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;

public sealed record AuthorizePlanningImplementationCommandResult(
    Guid AuthorizationId,
    Guid EscalationMessageId,
    Guid FinalProposalMessageId,
    Guid HumanInstructionMessageId,
    string Status,
    long? LatestEventSequence);
