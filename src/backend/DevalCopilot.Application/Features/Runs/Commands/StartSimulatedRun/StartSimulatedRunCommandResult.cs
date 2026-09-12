namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

public sealed record StartSimulatedRunCommandResult(Guid RunId, int ExecutionNumber);
