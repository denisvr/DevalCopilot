using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;

public sealed class InterruptPlanningSequencesCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<InterruptPlanningSequencesCommand, Result<InterruptPlanningSequencesCommandResult>>
{
    public async Task<Result<InterruptPlanningSequencesCommandResult>> HandleAsync(
        InterruptPlanningSequencesCommand command, CancellationToken cancellationToken)
    {
        // Every sequence that could still advance or drain, in a fixed order. A stored state this build does not recognize is never
        // materialized; the database check constraint makes that unreachable.
        var liveRows = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.State == PlanningSequenceState.Armed
                || candidate.State == PlanningSequenceState.PreparingStep
                || candidate.State == PlanningSequenceState.AwaitingStepResult
                || candidate.State == PlanningSequenceState.Stopping)
            .Select(candidate => new { candidate.Id, candidate.ArmedAtUtc })
            .ToListAsync(cancellationToken);

        // SQLite cannot order a DateTimeOffset, so the live sequences (a handful) are ordered here.
        var live = liveRows.OrderBy(candidate => candidate.ArmedAtUtc).Select(candidate => candidate.Id).ToList();
        if (live.Count == 0)
        {
            return Result<InterruptPlanningSequencesCommandResult>.Success(new InterruptPlanningSequencesCommandResult(0));
        }

        var interrupted = 0;
        foreach (var sequenceId in live)
        {
            if (await InterruptOneAsync(sequenceId, cancellationToken))
            {
                interrupted++;
            }
        }

        return Result<InterruptPlanningSequencesCommandResult>.Success(new InterruptPlanningSequencesCommandResult(interrupted));
    }

    private async Task<bool> InterruptOneAsync(Guid sequenceId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        var runId = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.Id == sequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is not { } run)
        {
            return false;
        }

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == run)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return false;
        }

        var sequence = await dbContext.PlanningSequences.SingleAsync(candidate => candidate.Id == sequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        if (!sequence.IsAuthorityLive)
        {
            return false;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var reserved = await dbContext.PlanningSequenceSteps
            .Where(candidate => candidate.SequenceId == sequenceId && candidate.State == PlanningSequenceStepState.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var step in reserved)
        {
            await dbContext.Entry(step).ReloadAsync(cancellationToken);
            var attemptExists = await dbContext.Attempts.AsNoTracking()
                .AnyAsync(candidate => candidate.Id == step.ReservedAttemptId, cancellationToken);
            if (attemptExists)
            {
                step.MarkUnresolved(PlanningSequenceReason.HostRestarted, nowUtc);
            }
            else
            {
                step.Revoke(PlanningSequenceReason.HostRestarted, nowUtc);
            }
        }

        sequence.End(PlanningSequenceState.Interrupted, PlanningSequenceReason.HostRestarted, detailCode: null, nowUtc);
        dbContext.Events.Add(PlanningSequenceEvents.Ended(sequence, nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The next iteration must not reuse tracked state of this sequence.
        dbContext.Entry(sequence).State = EntityState.Detached;
        foreach (var step in reserved)
        {
            dbContext.Entry(step).State = EntityState.Detached;
        }

        return true;
    }
}
