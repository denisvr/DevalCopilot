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
        // Each omitted or null choice independently keeps the fixed default; the validated whole minutes become
        // exactly that TimeSpan. Nothing is clamped or rounded here: the validator has already refused anything else.
        var recorded = await RunIntentRecorder.RecordAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.Objective,
            RunExecutionMode.ManualAgent,
            command.MaximumAgentAttempts ?? Run.DefaultMaximumAgentAttempts,
            command.MaximumAgentInvocationMinutes is { } minutes
                ? TimeSpan.FromMinutes(minutes)
                : Run.DefaultMaximumAgentInvocationTime,
            cancellationToken);

        return recorded.IsFailure
            ? Result<CreateManualRunCommandResult>.Failure(recorded.Errors[0])
            : Result<CreateManualRunCommandResult>.Success(
                new CreateManualRunCommandResult(recorded.Value.RunId, recorded.Value.ExecutionNumber));
    }
}
