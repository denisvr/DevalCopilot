using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

public sealed class StartSimulatedRunCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<StartSimulatedRunCommand, Result<StartSimulatedRunCommandResult>>
{
    public async Task<Result<StartSimulatedRunCommandResult>> HandleAsync(
        StartSimulatedRunCommand command,
        CancellationToken cancellationToken)
    {
        var recorded = await RunIntentRecorder.RecordAsync(
            dbContext, timeProvider, command.ProjectId, command.Objective, RunExecutionMode.Simulated, cancellationToken);

        return recorded.IsFailure
            ? Result<StartSimulatedRunCommandResult>.Failure(recorded.Errors[0])
            : Result<StartSimulatedRunCommandResult>.Success(
                new StartSimulatedRunCommandResult(recorded.Value.RunId, recorded.Value.ExecutionNumber));
    }
}
