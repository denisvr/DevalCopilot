using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;

/// <summary>
/// Durably records the run-scoped Claude agentic-turn-limit request and its human-authored change event
/// in one <c>SaveChangesAsync</c> call, with no provider call. It never mutates an already-claimed
/// attempt's own snapshot: only a later claim reads the new value, including while an earlier attempt is
/// still running.
///
/// <para>
/// <c>Run.Lifecycle</c> and the stored turn-limit text of <c>Run.RequestedClaudeMaxTurns</c> are EF concurrency tokens (see
/// <c>RunConfiguration</c>). A lifecycle transition, or another change of the request, committed between
/// this handler's read and its save makes the UPDATE match zero rows: the failed save rolls back the Run
/// change and the queued event together (this is a manual-transaction command, so the save owns its own
/// transaction), and the handler reports a terminal run as not editable and any other cause as a
/// retryable conflict. The UPDATE is forced even for an unchanged value, so a same-value request is
/// guarded by the same lifecycle check and still records its event.
/// </para>
/// </summary>
public sealed class SetClaudeMutationTurnLimitCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetClaudeMutationTurnLimitCommand, Result<SetClaudeMutationTurnLimitCommandResult>>
{
    public async Task<Result<SetClaudeMutationTurnLimitCommandResult>> HandleAsync(
        SetClaudeMutationTurnLimitCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetClaudeMutationTurnLimitCommandResult>.Failure(ClaudeMutationTurnLimitErrors.RunNotFound());
        }

        try
        {
            run.SetRequestedClaudeMaxTurns(command.MaxTurns);
        }
        catch (InvalidOperationException)
        {
            return Result<SetClaudeMutationTurnLimitCommandResult>.Failure(ClaudeMutationTurnLimitErrors.RunNotEditable());
        }

        dbContext.Entry(run).Property(Run.RequestedClaudeMaxTurnsStorageProperty).IsModified = true;

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.ClaudeMutationTurnLimitChanged,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new { maxTurns = run.RequestedClaudeMaxTurns }),
            timeProvider.GetUtcNow()));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var lifecycle = await dbContext.Runs
                .AsNoTracking()
                .Where(candidate => candidate.Id == command.RunId)
                .Select(candidate => (RunLifecycle?)candidate.Lifecycle)
                .SingleOrDefaultAsync(cancellationToken);

            return lifecycle switch
            {
                null => Result<SetClaudeMutationTurnLimitCommandResult>.Failure(ClaudeMutationTurnLimitErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetClaudeMutationTurnLimitCommandResult>.Failure(ClaudeMutationTurnLimitErrors.ConcurrentChange()),
                _ => Result<SetClaudeMutationTurnLimitCommandResult>.Failure(ClaudeMutationTurnLimitErrors.RunNotEditable()),
            };
        }

        return Result<SetClaudeMutationTurnLimitCommandResult>.Success(
            new SetClaudeMutationTurnLimitCommandResult(run.RequestedClaudeMaxTurns));
    }
}
