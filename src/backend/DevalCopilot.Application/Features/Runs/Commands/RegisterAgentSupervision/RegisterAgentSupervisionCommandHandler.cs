using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;

public sealed class RegisterAgentSupervisionCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<RegisterAgentSupervisionCommand, Result<RegisterAgentSupervisionCommandResult>>
{
    public async Task<Result<RegisterAgentSupervisionCommandResult>> HandleAsync(
        RegisterAgentSupervisionCommand command, CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty || command.AttemptId == Guid.Empty || command.HostInstanceId == Guid.Empty
            || command.ObserverId == Guid.Empty)
        {
            return Failure(AgentSupervisionErrors.InvalidRequest());
        }

        // Only the running host may start an observation: an identity of another launch is never written.
        if (command.HostInstanceId != host.Id)
        {
            return Failure(AgentSupervisionErrors.NotCurrentHost());
        }

        // A replay or a competing identity is decided without taking the write lock: a supervisor that re-enters a still unresolved
        // attempt on every poll must not make the observation a write load.
        var known = await dbContext.AgentSupervisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AttemptId == command.AttemptId, cancellationToken);
        if (known is not null)
        {
            return Replay(command, known);
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row takes the database write lock first, so the attempt facts read below are the ones this registration commits with.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(AgentSupervisionErrors.AttemptNotObservable());
        }

        var existing = await dbContext.AgentSupervisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AttemptId == command.AttemptId, cancellationToken);
        if (existing is not null)
        {
            return Replay(command, existing);
        }

        var attempt = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (attempt is null || attempt.RunId != command.RunId || !AgentSupervisionAttempts.IsObservable(attempt))
        {
            return Failure(AgentSupervisionErrors.AttemptNotObservable());
        }

        var nowUtc = timeProvider.GetUtcNow();
        AgentSupervision receipt;
        try
        {
            receipt = AgentSupervision.Register(
                Guid.NewGuid(), command.RunId, command.AttemptId, command.HostInstanceId, command.ObserverId, nowUtc);
        }
        catch (ArgumentOutOfRangeException)
        {
            // An unrepresentable clock or deadline is refused, never fabricated as a fresh heartbeat.
            return Failure(AgentSupervisionErrors.ChronologyInvalid());
        }

        // The receipt and its one registration event commit together or not at all.
        var registered = AgentSupervisionEvents.Registered(receipt, nowUtc);
        dbContext.AgentSupervisions.Add(receipt);
        dbContext.Events.Add(registered);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure(AgentSupervisionErrors.PersistenceFailed());
        }

        await AgentSupervisionWrites.NotifyAsync(eventNotifier, receipt.RunId, registered.Sequence, cancellationToken);
        return Result<RegisterAgentSupervisionCommandResult>.Success(
            new RegisterAgentSupervisionCommandResult(receipt.Id, receipt.Revision, Replayed: false));
    }

    private static Result<RegisterAgentSupervisionCommandResult> Replay(
        RegisterAgentSupervisionCommand command, AgentSupervision existing) =>
        existing.IsIdenticalRegistration(command.RunId, command.AttemptId, command.HostInstanceId, command.ObserverId)
            ? Result<RegisterAgentSupervisionCommandResult>.Success(
                new RegisterAgentSupervisionCommandResult(existing.Id, existing.Revision, Replayed: true))
            : Failure(AgentSupervisionErrors.Conflict());

    private static Result<RegisterAgentSupervisionCommandResult> Failure(Error error) =>
        Result<RegisterAgentSupervisionCommandResult>.Failure(error);
}
