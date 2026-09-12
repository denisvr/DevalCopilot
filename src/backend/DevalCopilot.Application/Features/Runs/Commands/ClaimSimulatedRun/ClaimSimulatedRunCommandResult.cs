namespace DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;

public sealed record ClaimSimulatedRunCommandResult(Guid AttemptId, int AttemptNumber);
