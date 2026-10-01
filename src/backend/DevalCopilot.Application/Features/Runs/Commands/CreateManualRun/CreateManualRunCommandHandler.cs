using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;

public sealed class CreateManualRunCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<CreateManualRunCommand, Result<CreateManualRunCommandResult>>
{
    public async Task<Result<CreateManualRunCommandResult>> HandleAsync(
        CreateManualRunCommand command,
        CancellationToken cancellationToken)
    {
        var recorded = await RunIntentRecorder.RecordAsync(
            dbContext, timeProvider, command.ProjectId, command.Objective, RunExecutionMode.ManualAgent, cancellationToken);

        return recorded.IsFailure
            ? Result<CreateManualRunCommandResult>.Failure(recorded.Errors[0])
            : Result<CreateManualRunCommandResult>.Success(
                new CreateManualRunCommandResult(recorded.Value.RunId, recorded.Value.ExecutionNumber));
    }
}
