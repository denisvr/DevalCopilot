using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ResolveLocalExecutionSequenceStepPreparation;

public sealed class ResolveLocalExecutionSequenceStepPreparationCommandHandler(
    IDevalCopilotDbContext dbContext,
    ILocalExecutionStepProbe stepProbe,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ResolveLocalExecutionSequenceStepPreparationCommand, Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>>
{
    public async Task<Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>> HandleAsync(
        ResolveLocalExecutionSequenceStepPreparationCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.Ticket;

        // The independent read comes first and through its own connection: a cancellation or an exception never establishes that the
        // claim did not commit.
        var probed = await stepProbe.CheckAsync(ticket.StepId, cancellationToken);

        var runId = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == ticket.SequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is not { } run)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == run)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var sequence = await dbContext.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == ticket.SequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        var step = await dbContext.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        await dbContext.Entry(step).ReloadAsync(cancellationToken);

        if (probed == LocalExecutionStepProbeResult.Claimed
            || step.State != LocalExecutionSequenceStepState.Reserved
            || step.PreparationOwnerToken != ticket.OwnerToken
            || sequence.State != LocalExecutionSequenceState.PreparingStep)
        {
            // The claim committed, or a Stop, an expiry or a restart already settled this step: nothing is written.
            return Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>.Success(
                new ResolveLocalExecutionSequenceStepPreparationCommandResult(sequence.State, step.State, Changed: false));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var detail = LocalExecutionSequenceReason.NormalizeDetail(command.FailureCode);
        string reason;
        if (probed == LocalExecutionStepProbeResult.NotClaimed)
        {
            // ADR-0062: the independent absence probe is read first, but only the write-locked proof of the unused reservation retires a
            // queued step; any contradiction leaves it held as Unresolved.
            reason = ReasonFor(command.FailureCode);
            var outcome = await LocalExecutionReservationRetirement.RetireOrHoldAsync(
                dbContext, sequence, step, reason, LocalExecutionSequenceReason.PreparationUnresolved, nowUtc, cancellationToken);
            if (outcome == LocalExecutionReservationRetirement.Outcome.NotQueued)
            {
                step.Revoke(reason, nowUtc);
            }
            else if (outcome == LocalExecutionReservationRetirement.Outcome.Held)
            {
                reason = LocalExecutionSequenceReason.PreparationUnresolved;
            }
        }
        else
        {
            reason = LocalExecutionSequenceReason.PreparationUnresolved;
            await LocalExecutionReservationRetirement.HoldAsync(dbContext, sequence, step, reason, nowUtc, cancellationToken);
        }

        sequence.End(LocalExecutionSequenceState.NeedsAttention, reason, detail, nowUtc);
        var ended = LocalExecutionSequenceEvents.Ended(sequence, nowUtc);
        dbContext.Events.Add(ended);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure(LocalExecutionSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(run, ended.Sequence, cancellationToken);
        return Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>.Success(
            new ResolveLocalExecutionSequenceStepPreparationCommandResult(sequence.State, step.State, Changed: true));
    }

    /// <summary>The fixed coordinator reason of an absent claim. The original stable error code is preserved separately as the detail,
    /// so a capacity, lease, budget or source refusal never hides its own reason.</summary>
    private static string ReasonFor(string? failureCode) => failureCode switch
    {
        LocalExecutionSequenceErrors.StepExpiredCode => LocalExecutionSequenceReason.PreparationExpired,
        LocalExecutionSequenceErrors.StepSourceChangedCode => LocalExecutionSequenceReason.AuthorityChanged,
        "execution_capacity.limit_reached" or "execution_capacity.unavailable" or "workspace_execution.agent_attempt_active"
            or "workspace_execution.verification_active" or "workspace_execution.owners_conflicted" or "workspace_execution.unavailable"
            or "workspace_execution.commit_reserved" => LocalExecutionSequenceReason.CapacityRefused,
        LocalExecutionSequenceErrors.StepAuthorityLostCode or LocalExecutionSequenceErrors.StepInputMismatchCode
            or LocalExecutionSequenceErrors.PersistenceFailedCode or "attempts.persistence_failed" => LocalExecutionSequenceReason.PreparationFailed,
        _ => LocalExecutionSequenceReason.PreparationRefused,
    };

    private static Result<ResolveLocalExecutionSequenceStepPreparationCommandResult> Failure(Error error) =>
        Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>.Failure(error);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// transition into a reported failure. Cancellation still propagates.</summary>
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
