using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;

/// <summary>
/// Durably records the run-scoped advisory Codex account-usage warning and its human-authored change event in one
/// <c>SaveChangesAsync</c> call, with no provider call and no observation. <c>Run.Lifecycle</c> and the execution mode are EF
/// concurrency tokens, so a lifecycle transition committed between this handler's read and its save makes the UPDATE match zero rows
/// and rolls the Run change and the queued event back together; a terminal run is then not editable and any other cause is a
/// retryable conflict. The UPDATE is forced even for an unchanged value, so a same-value request is guarded the same way and still
/// records its event, and a valid set or clear repairs a malformed stored value. The warning column is deliberately not a
/// concurrency token: a warning write must never make a claim's own Run UPDATE fail, and SQLite serializes write transactions, so two
/// concurrent warning writes each commit their own value and event atomically and the last committed value and event agree.
/// </summary>
public sealed class SetCodexAccountUsageWarningCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetCodexAccountUsageWarningCommand, Result<SetCodexAccountUsageWarningCommandResult>>
{
    public async Task<Result<SetCodexAccountUsageWarningCommandResult>> HandleAsync(
        SetCodexAccountUsageWarningCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetCodexAccountUsageWarningCommandResult>.Failure(CodexAccountUsageWarningErrors.RunNotFound());
        }

        // Only a run that admits Agent work has anything to warn about; the stored mode is read afresh, never from the tracked Run.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<SetCodexAccountUsageWarningCommandResult>.Failure(executionModeError);
        }

        try
        {
            run.SetCodexAccountUsageWarningPercent(command.Percent);
        }
        catch (InvalidOperationException)
        {
            return Result<SetCodexAccountUsageWarningCommandResult>.Failure(CodexAccountUsageWarningErrors.RunNotEditable());
        }

        dbContext.Entry(run).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.CodexAccountUsageWarningChanged,
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
                null => Result<SetCodexAccountUsageWarningCommandResult>.Failure(CodexAccountUsageWarningErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetCodexAccountUsageWarningCommandResult>.Failure(CodexAccountUsageWarningErrors.ConcurrentChange()),
                _ => Result<SetCodexAccountUsageWarningCommandResult>.Failure(CodexAccountUsageWarningErrors.RunNotEditable()),
            };
        }

        return Result<SetCodexAccountUsageWarningCommandResult>.Success(
            new SetCodexAccountUsageWarningCommandResult(command.Percent));
    }
}
