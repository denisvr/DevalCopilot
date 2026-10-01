using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;

public sealed class CompleteSimulatedRunCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<CompleteSimulatedRunCommand, Result<long>>
{
    public async Task<Result<long>> HandleAsync(CompleteSimulatedRunCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null)
        {
            return Result<long>.Failure(Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.RunId != run.Id)
        {
            return Result<long>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        // Only the deterministic simulation may terminalize a run through this command. An Agent or Process
        // attempt (and so a manual Agent run) is concluded exclusively by its own result handlers.
        if (attempt.Kind != AttemptKind.Simulated)
        {
            return Result<long>.Failure(Error.Conflict(
                "attempts.simulated_completion_requires_simulated_attempt",
                "A simulated completion can only conclude a Simulated attempt."));
        }

        // Read afresh (never from the tracked Run) and guarded by the mode's concurrency token.
        var executionModeError = await CurrentRunExecutionMode.ReadAndGuardSimulationAsync(dbContext, run, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<long>.Failure(executionModeError);
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<long>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot be completed."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<long>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot be completed."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        run.Complete(nowUtc);
        attempt.Complete(nowUtc);

        var payload = JsonSerializer.Serialize(new { summary = "The simulated run reached a terminal completed state." });
        var runEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, attempt.Id, RunEventType.RunCompleted, ParticipantIdentity.ForOrchestrator(), payload, nowUtc);
        dbContext.Events.Add(runEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Events.Remove(runEvent);
            return Result<long>.Failure(
                await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                    ? CurrentRunExecutionMode.ChangedDuringClaim()
                    : Error.Conflict("runs.not_running", "The run changed and cannot be completed."));
        }

        return Result<long>.Success(runEvent.Sequence);
    }
}
