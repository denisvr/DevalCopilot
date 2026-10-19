using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveAgentSupervision;

public sealed class ObserveAgentSupervisionCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ObserveAgentSupervisionCommand, Result<ObserveAgentSupervisionCommandResult>>
{
    public async Task<Result<ObserveAgentSupervisionCommandResult>> HandleAsync(
        ObserveAgentSupervisionCommand command, CancellationToken cancellationToken)
    {
        var known = await dbContext.AgentSupervisions.AsNoTracking()
            .Where(candidate => candidate.Id == command.SupervisionId)
            .Select(candidate => new { Closed = candidate.ClosedAtUtc != null, candidate.AttentionReason })
            .SingleOrDefaultAsync(cancellationToken);
        if (known is null)
        {
            return Failure(AgentSupervisionErrors.ObservationNotFound());
        }

        if (known.Closed)
        {
            return Success(changed: false, closed: true, known.AttentionReason);
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        if (!await AgentSupervisionWrites.LockOpenAsync(dbContext, command.SupervisionId, cancellationToken))
        {
            // Closed by its own observer or startup between the read and the lock: nothing to observe.
            return Success(changed: false, closed: true, known.AttentionReason);
        }

        var receipt = await dbContext.AgentSupervisions.SingleAsync(candidate => candidate.Id == command.SupervisionId, cancellationToken);
        var attempt = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == receipt.AttemptId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();
        var events = new List<RunEvent>();
        var closed = false;
        if (receipt.HostInstanceId != host.Id)
        {
            // An old host's observation is never adopted, renewed or trusted as fresh.
            closed = AgentSupervisionClosure.TryClose(receipt, attempt, nowUtc, hostInterrupted: true, events);
            if (!closed)
            {
                return Failure(AgentSupervisionErrors.ChronologyInvalid());
            }
        }
        else if (attempt is not null && AgentSupervisionAttempts.IsTerminal(attempt))
        {
            // The Attempt's own terminal facts govern: a timely result observed late is not attention and a late one is.
            closed = AgentSupervisionClosure.TryClose(receipt, attempt, nowUtc, hostInterrupted: false, events);
            if (!closed)
            {
                return Failure(AgentSupervisionErrors.ChronologyInvalid());
            }
        }
        else
        {
            var assessment = AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost: true);
            if (!assessment.ChronologyValid)
            {
                return Failure(AgentSupervisionErrors.ChronologyInvalid());
            }

            AgentSupervisionWrites.RecordFirstAttention(receipt, assessment, nowUtc, events);
        }

        var changed = closed || events.Count > 0;
        if (!changed)
        {
            return Success(changed: false, closed: false, receipt.AttentionReason);
        }

        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure(AgentSupervisionErrors.PersistenceFailed());
        }

        if (events.Count > 0)
        {
            await AgentSupervisionWrites.NotifyAsync(eventNotifier, receipt.RunId, events[^1].Sequence, cancellationToken);
        }

        return Success(changed: true, closed, receipt.AttentionReason);
    }

    private static Result<ObserveAgentSupervisionCommandResult> Success(bool changed, bool closed, string? attentionReason) =>
        Result<ObserveAgentSupervisionCommandResult>.Success(new ObserveAgentSupervisionCommandResult(changed, closed, attentionReason));

    private static Result<ObserveAgentSupervisionCommandResult> Failure(Error error) =>
        Result<ObserveAgentSupervisionCommandResult>.Failure(error);
}
