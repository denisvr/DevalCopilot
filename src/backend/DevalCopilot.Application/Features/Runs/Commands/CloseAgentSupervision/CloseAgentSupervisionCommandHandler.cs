using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CloseAgentSupervision;

public sealed class CloseAgentSupervisionCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<CloseAgentSupervisionCommand, Result<CloseAgentSupervisionCommandResult>>
{
    public async Task<Result<CloseAgentSupervisionCommandResult>> HandleAsync(
        CloseAgentSupervisionCommand command, CancellationToken cancellationToken)
    {
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
        var attempt = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == receipt.AttemptId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow();
        var events = new List<RunEvent>();
        if (!AgentSupervisionClosure.TryClose(receipt, attempt, nowUtc, hostInterrupted: false, events))
        {
            return Failure(AgentSupervisionErrors.ChronologyInvalid());
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

        return Result<CloseAgentSupervisionCommandResult>.Success(
            new CloseAgentSupervisionCommandResult(receipt.ClosureReason!, receipt.AttentionReason, receipt.Revision));
    }

    private static Result<CloseAgentSupervisionCommandResult> Failure(Error error) =>
        Result<CloseAgentSupervisionCommandResult>.Failure(error);
}
