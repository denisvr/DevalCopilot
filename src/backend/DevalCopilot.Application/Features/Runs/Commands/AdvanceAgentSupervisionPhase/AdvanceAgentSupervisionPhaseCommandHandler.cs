using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.AdvanceAgentSupervisionPhase;

public sealed class AdvanceAgentSupervisionPhaseCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<AdvanceAgentSupervisionPhaseCommand, Result<AdvanceAgentSupervisionPhaseCommandResult>>
{
    public async Task<Result<AdvanceAgentSupervisionPhaseCommandResult>> HandleAsync(
        AdvanceAgentSupervisionPhaseCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Phase))
        {
            return Failure(AgentSupervisionErrors.InvalidRequest());
        }

        if (command.HostInstanceId != host.Id)
        {
            return Failure(AgentSupervisionErrors.NotCurrentHost());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        if (!await AgentSupervisionWrites.LockOpenOwnedAsync(
                dbContext, command.AttemptId, command.HostInstanceId, command.ObserverId, cancellationToken))
        {
            return Failure(await AgentSupervisionWrites.RefusalAsync(
                dbContext, command.AttemptId, command.HostInstanceId, command.ObserverId, cancellationToken));
        }

        var receipt = await dbContext.AgentSupervisions.SingleAsync(candidate => candidate.AttemptId == command.AttemptId, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();

        // The previous phase's deadline and freshness are judged first, against the row the previous writer left. The first attention
        // is recorded even when the move itself is then refused.
        var assessment = AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost: true);
        if (!assessment.ChronologyValid)
        {
            return Failure(AgentSupervisionErrors.ChronologyInvalid());
        }

        var events = new List<RunEvent>();
        AgentSupervisionWrites.RecordFirstAttention(receipt, assessment, nowUtc, events);

        // The actual reserved invocation timeout of this very attempt, read in the same transaction; never a caller-supplied value.
        var reservedTimeout = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.Id == receipt.AttemptId)
            .Select(candidate => candidate.AgentTimeout)
            .SingleOrDefaultAsync(cancellationToken);
        var deadline = AgentSupervisionPolicy.PhaseDeadlineOf(command.Phase, nowUtc, reservedTimeout);
        if (deadline is null || AgentSupervisionPolicy.Rank(command.Phase) <= AgentSupervisionPolicy.Rank(receipt.Phase))
        {
            return await RefuseAsync(transaction, receipt, events, AgentSupervisionErrors.PhaseNotForward(), cancellationToken);
        }

        // Any first attention found above is recorded before the phase entry, and both commit with the receipt or not at all.
        var attentionRecorded = events.Count > 0;
        receipt.AdvancePhase(command.Phase, nowUtc, deadline.Value);
        events.Add(AgentSupervisionEvents.PhaseEntered(receipt, nowUtc));
        if (!await TryCommitAsync(transaction, events, cancellationToken))
        {
            return Failure(AgentSupervisionErrors.PersistenceFailed());
        }

        await NotifyAsync(receipt, events, cancellationToken);
        return Result<AdvanceAgentSupervisionPhaseCommandResult>.Success(
            new AdvanceAgentSupervisionPhaseCommandResult(receipt.Phase, receipt.PhaseDeadlineUtc, receipt.Revision, attentionRecorded));
    }

    /// <summary>A refused move still keeps the attention it found: that fact is committed, then the refusal is returned.</summary>
    private async Task<Result<AdvanceAgentSupervisionPhaseCommandResult>> RefuseAsync(
        IDbContextTransaction transaction,
        AgentSupervision receipt,
        List<RunEvent> events,
        Error refusal,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Failure(refusal);
        }

        if (!await TryCommitAsync(transaction, events, cancellationToken))
        {
            return Failure(AgentSupervisionErrors.PersistenceFailed());
        }

        await NotifyAsync(receipt, events, cancellationToken);
        return Failure(refusal);
    }

    private async Task<bool> TryCommitAsync(IDbContextTransaction transaction, List<RunEvent> events, CancellationToken cancellationToken)
    {
        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task NotifyAsync(AgentSupervision receipt, List<RunEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count > 0)
        {
            await AgentSupervisionWrites.NotifyAsync(eventNotifier, receipt.RunId, events[^1].Sequence, cancellationToken);
        }
    }

    private static Result<AdvanceAgentSupervisionPhaseCommandResult> Failure(Error error) =>
        Result<AdvanceAgentSupervisionPhaseCommandResult>.Failure(error);
}
