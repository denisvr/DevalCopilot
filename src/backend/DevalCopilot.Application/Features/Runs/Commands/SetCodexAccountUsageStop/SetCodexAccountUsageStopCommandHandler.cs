using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;

/// <summary>
/// Durably records the run-scoped Codex account-usage stop and its human-authored change event in one <c>SaveChangesAsync</c> call,
/// with no provider call and no observation. It never mutates an already-claimed attempt's snapshot: only a later claim reads the new
/// value. <c>Run.Lifecycle</c>, the execution mode and the stored setting are EF concurrency tokens, so a lifecycle transition or
/// another change committed between this handler's read and its save makes the UPDATE match zero rows and rolls the Run change and the
/// queued event back together; a terminal run is then not editable and any other cause is a retryable conflict. The UPDATE is forced
/// even for an unchanged value, so a same-value request is guarded the same way and still records its event, and a valid set or clear
/// repairs a malformed stored value.
/// </summary>
public sealed class SetCodexAccountUsageStopCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetCodexAccountUsageStopCommand, Result<SetCodexAccountUsageStopCommandResult>>
{
    public async Task<Result<SetCodexAccountUsageStopCommandResult>> HandleAsync(
        SetCodexAccountUsageStopCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetCodexAccountUsageStopCommandResult>.Failure(CodexAccountUsageStopErrors.RunNotFound());
        }

        // Only a run that admits Agent work has anything to guard; the stored mode is read afresh, never from the tracked Run.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<SetCodexAccountUsageStopCommandResult>.Failure(executionModeError);
        }

        try
        {
            run.SetCodexAccountUsageStopPercent(command.Percent);
        }
        catch (InvalidOperationException)
        {
            return Result<SetCodexAccountUsageStopCommandResult>.Failure(CodexAccountUsageStopErrors.RunNotEditable());
        }

        dbContext.Entry(run).Property(Run.CodexAccountUsageStopStorageProperty).IsModified = true;

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.CodexAccountUsageStopChanged,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new { percent = command.Percent }),
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
                null => Result<SetCodexAccountUsageStopCommandResult>.Failure(CodexAccountUsageStopErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetCodexAccountUsageStopCommandResult>.Failure(CodexAccountUsageStopErrors.ConcurrentChange()),
                _ => Result<SetCodexAccountUsageStopCommandResult>.Failure(CodexAccountUsageStopErrors.RunNotEditable()),
            };
        }

        return Result<SetCodexAccountUsageStopCommandResult>.Success(new SetCodexAccountUsageStopCommandResult(command.Percent));
    }
}
