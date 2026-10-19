using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;

public sealed class CloseInterruptedAgentSupervisionsCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, AgentSupervisionHostInstance host)
    : ICommandHandler<CloseInterruptedAgentSupervisionsCommand, Result<CloseInterruptedAgentSupervisionsCommandResult>>
{
    public async Task<Result<CloseInterruptedAgentSupervisionsCommandResult>> HandleAsync(
        CloseInterruptedAgentSupervisionsCommand command, CancellationToken cancellationToken)
    {
        // Only the open observations of another launch, in a fixed order. Another launch's freshness is never trusted.
        var open = await dbContext.AgentSupervisions.AsNoTracking()
            .Where(candidate => candidate.ClosedAtUtc == null && candidate.HostInstanceId != host.Id)
            .Select(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var closed = 0;
        var unclosed = 0;
        foreach (var supervisionId in open.Order())
        {
            if (await CloseOneAsync(supervisionId, cancellationToken))
            {
                closed++;
            }
            else
            {
                unclosed++;
            }
        }

        return Result<CloseInterruptedAgentSupervisionsCommandResult>.Success(
            new CloseInterruptedAgentSupervisionsCommandResult(closed, unclosed));
    }

    private async Task<bool> CloseOneAsync(Guid supervisionId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        if (!await AgentSupervisionWrites.LockOpenAsync(dbContext, supervisionId, cancellationToken))
        {
            return true;
        }

        var receipt = await dbContext.AgentSupervisions.SingleAsync(candidate => candidate.Id == supervisionId, cancellationToken);
        var events = new List<RunEvent>();
        if (!AgentSupervisionClosure.TryClose(receipt, attempt: null, timeProvider.GetUtcNow(), hostInterrupted: true, events))
        {
            dbContext.Entry(receipt).State = EntityState.Detached;
            return false;
        }

        dbContext.Events.AddRange(events);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The next iteration must not reuse tracked state of this observation.
        dbContext.Entry(receipt).State = EntityState.Detached;
        foreach (var runEvent in events)
        {
            dbContext.Entry(runEvent).State = EntityState.Detached;
        }

        return true;
    }
}
