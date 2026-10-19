using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;

public sealed class HeartbeatAgentSupervisionCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<HeartbeatAgentSupervisionCommand, Result<HeartbeatAgentSupervisionCommandResult>>
{
    public async Task<Result<HeartbeatAgentSupervisionCommandResult>> HandleAsync(
        HeartbeatAgentSupervisionCommand command, CancellationToken cancellationToken)
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
        var nowUtc = timeProvider.GetUtcNow();

        // The previous deadline and freshness are judged against the row the previous writer left, before this beat can refresh it.
        var assessment = AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost: true);
        if (!assessment.ChronologyValid)
        {
            return Failure(AgentSupervisionErrors.ChronologyInvalid());
        }

        var events = new List<RunEvent>();
        AgentSupervisionWrites.RecordFirstAttention(receipt, assessment, nowUtc, events);
        receipt.RecordHeartbeat(nowUtc);

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

        return Result<HeartbeatAgentSupervisionCommandResult>.Success(
            new HeartbeatAgentSupervisionCommandResult(receipt.Revision, events.Count > 0));
    }

    private static Result<HeartbeatAgentSupervisionCommandResult> Failure(Error error) =>
        Result<HeartbeatAgentSupervisionCommandResult>.Failure(error);
}
