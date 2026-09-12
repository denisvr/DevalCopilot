using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;

public sealed class RecordSimulatedAgentStepCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordSimulatedAgentStepCommand, Result<long>>
{
    public async Task<Result<long>> HandleAsync(RecordSimulatedAgentStepCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);

        if (run is null)
        {
            return Result<long>.Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != run.Id)
        {
            return Result<long>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<long>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a step."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<long>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot advance."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        run.AdvanceStage(command.Stage, command.Actor, nowUtc);

        var payload = JsonSerializer.Serialize(new { summary = command.Summary });
        var runEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, command.AttemptId, command.EventType, command.Actor, payload, nowUtc);
        dbContext.Events.Add(runEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<long>.Success(runEvent.Sequence);
    }
}
