using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;

/// <summary>
/// Ends one inactive manual Agent run as Abandoned and records exactly one Human-authored event, atomically (ADR-0031). No external
/// work belongs here: every decision is made from fresh untracked reads inside one short transaction whose first statement is a no-op
/// write that takes the database write lock, so no claim, admission or competing abandonment can commit between the reads and this
/// save.
///
/// <para>
/// Both directions of the race with Agent and local-commit admission are decided by that serialization. When an admission wins, its
/// Running attempt, reserved workspace or open operation is read here and the abandonment is refused with nothing written. When this
/// save wins, the run's Lifecycle (an EF concurrency token that every claim's Run guard or in-transaction lifecycle read observes)
/// has changed, so a claim that decided earlier can no longer commit an attempt or an operation, and no further dispatch is possible.
/// The same normalized reason against an already coherent abandonment returns the recorded result without a new event or time; a
/// different reason conflicts. A refusal records nothing; a rolled-back save records nothing.
/// </para>
/// </summary>
public sealed class AbandonManualRunCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<AbandonManualRunCommand, Result<AbandonManualRunCommandResult>>
{
    public async Task<Result<AbandonManualRunCommandResult>> HandleAsync(
        AbandonManualRunCommand command, CancellationToken cancellationToken)
    {
        if (!RunAbandonmentPolicy.TryNormalizeReason(command.Reason, out var reason))
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.InvalidReason());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it, so
        // it is atomic with the transition saved at the end. Zero rows means the run does not exist.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.RunNotFound());
        }

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, command.RunId, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.RunNotManual());
        }

        if (await dbContext.Runs.AsNoTracking().AnyAsync(
                candidate => candidate.Id == command.RunId && candidate.Lifecycle == RunLifecycle.Abandoned, cancellationToken))
        {
            return await ArbitrateAsync(command.RunId, reason, cancellationToken);
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(
                candidate => candidate.Id == command.RunId
                    && (candidate.Lifecycle == RunLifecycle.Created || candidate.Lifecycle == RunLifecycle.Running),
                cancellationToken))
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.RunNotAbandonable());
        }

        var projectId = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == command.RunId)
            .Select(candidate => candidate.ProjectId)
            .SingleAsync(cancellationToken);
        var blocker = await RunAbandonmentAuthority.FindBlockerAsync(dbContext, projectId, cancellationToken);
        if (blocker is not null)
        {
            return Result<AbandonManualRunCommandResult>.Failure(blocker);
        }

        // The tracked write target may be an older copy of this Run that the same context loaded before the write lock (a tracking query
        // returns the instance it already holds). Refresh exactly this one entity from the locked database, so the transition below and
        // the original values EF compares at the save are the committed ones; the rest of the tracker is left alone.
        var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        await dbContext.Entry(run).ReloadAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            run.Abandon(reason, nowUtc);
        }
        catch (InvalidOperationException)
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.RunNotAbandonable());
        }

        var recorded = RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.RunAbandoned,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new { reason }),
            nowUtc);
        dbContext.Events.Add(recorded);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.ConcurrentChange());
        }

        await NotifyAsync(run.Id, recorded.Sequence, cancellationToken);

        return Result<AbandonManualRunCommandResult>.Success(
            new AbandonManualRunCommandResult(run.Id, run.ExecutionNumber, reason, nowUtc));
    }

    /// <summary>The run is already Abandoned: only a coherent record with the same normalized reason replays; a different reason
    /// conflicts and an incoherent record is never treated as an abandonment. Nothing is written.</summary>
    private async Task<Result<AbandonManualRunCommandResult>> ArbitrateAsync(
        Guid runId, string reason, CancellationToken cancellationToken)
    {
        var reading = await RunAbandonmentReader.ReadAsync(dbContext, runId, cancellationToken);
        if (reading is not { Coherent: true, AbandonedAtUtc: { } abandonedAtUtc, Reason: { } recordedReason })
        {
            return Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.AbandonmentIncoherent());
        }

        return string.Equals(recordedReason, reason, StringComparison.Ordinal)
            ? Result<AbandonManualRunCommandResult>.Success(
                new AbandonManualRunCommandResult(runId, reading.ExecutionNumber, recordedReason, abandonedAtUtc))
            : Result<AbandonManualRunCommandResult>.Failure(RunAbandonmentErrors.ReasonConflict());
    }

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// abandonment into a reported failure. Cancellation still propagates.</summary>
    private async Task NotifyAsync(Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }
}
