using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;

/// <summary>
/// Durably records the run-scoped Claude model-alias/effort request pair and its change event in one
/// <c>SaveChangesAsync</c> call. The alias set is closed and validated without any provider call:
/// this never claims the alias is available to the signed-in account. It never mutates an
/// already-claimed attempt's own snapshot — only a later claim reads the new value.
///
/// <para>
/// <c>Run.Lifecycle</c>, <c>Run.RequestedClaudeModel</c>, and <c>Run.RequestedClaudeEffort</c> are EF concurrency tokens (see
/// <c>RunConfiguration</c>). A lifecycle transition, or another preference change, committed between
/// this handler's read and its save makes the UPDATE match zero rows: the failed save rolls back
/// the Run change and the queued event together (this is a manual-transaction command, so the save
/// owns its own transaction), and the handler reports a terminal run as not editable and any other
/// cause as a retryable conflict.
/// </para>
/// </summary>
public sealed class SetClaudeModelPreferenceCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetClaudeModelPreferenceCommand, Result<SetClaudeModelPreferenceCommandResult>>
{
    public async Task<Result<SetClaudeModelPreferenceCommandResult>> HandleAsync(
        SetClaudeModelPreferenceCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetClaudeModelPreferenceCommandResult>.Failure(ClaudeModelPreferenceErrors.RunNotFound());
        }

        try
        {
            run.SetRequestedClaudeModelRequest(command.RequestedModel, command.RequestedEffort);
        }
        catch (InvalidOperationException)
        {
            return Result<SetClaudeModelPreferenceCommandResult>.Failure(ClaudeModelPreferenceErrors.RunNotEditable());
        }

        // Force the Run UPDATE even when the alias is unchanged, so its concurrency-token WHERE
        // clause always guards the lifecycle: a no-op set must not append an event to a Run that
        // became terminal after the read above.
        dbContext.Entry(run).Property(candidate => candidate.RequestedClaudeModel).IsModified = true;
        dbContext.Entry(run).Property(candidate => candidate.RequestedClaudeEffort).IsModified = true;

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.ClaudeModelPreferenceChanged,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new { requestedModel = run.RequestedClaudeModel, requestedEffort = run.RequestedClaudeEffort }),
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
                null => Result<SetClaudeModelPreferenceCommandResult>.Failure(ClaudeModelPreferenceErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetClaudeModelPreferenceCommandResult>.Failure(ClaudeModelPreferenceErrors.ConcurrentChange()),
                _ => Result<SetClaudeModelPreferenceCommandResult>.Failure(ClaudeModelPreferenceErrors.RunNotEditable()),
            };
        }

        return Result<SetClaudeModelPreferenceCommandResult>.Success(new SetClaudeModelPreferenceCommandResult(run.RequestedClaudeModel, run.RequestedClaudeEffort));
    }
}
