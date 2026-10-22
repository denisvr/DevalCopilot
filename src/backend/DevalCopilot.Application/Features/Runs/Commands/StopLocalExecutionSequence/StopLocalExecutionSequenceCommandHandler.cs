using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;

/// <summary>
/// Records "Stop after current operation" for one exact sequence (ADR-0060). One short transaction whose first statement is a no-op
/// write on the run row takes the database write lock, so a claim, an expiry, a classification or a restart that committed first is
/// read here and one that arrives later contends on the revisions this save advances. The decision per state is exact: Armed ends as
/// Stopped; a reserved but unclaimed step is revoked and the sequence is Stopped, consuming no operation, budget or grant; a claimed
/// step keeps its authority to dispatch and finish and the sequence becomes Stopping; every other state is returned unchanged.
/// </summary>
public sealed class StopLocalExecutionSequenceCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<StopLocalExecutionSequenceCommand, Result<StopLocalExecutionSequenceCommandResult>>
{
    public async Task<Result<StopLocalExecutionSequenceCommandResult>> HandleAsync(
        StopLocalExecutionSequenceCommand command, CancellationToken cancellationToken)
    {
        // A foreign or unknown sequence is one fixed non-disclosing refusal.
        var exists = await dbContext.LocalExecutionSequences.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == command.SequenceId && candidate.RunId == command.RunId, cancellationToken);
        if (!exists)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var sequence = await dbContext.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == command.SequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);

        if (sequence.State is not (LocalExecutionSequenceState.Armed or LocalExecutionSequenceState.PreparingStep
            or LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.WaitingForAdmission))
        {
            // Stopping already, or ended (approved, escalated, stopped, attention or interrupted): idempotent and unchanged.
            return Success(sequence, changed: false);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var events = new List<RunEvent>();
        switch (sequence.State)
        {
            case LocalExecutionSequenceState.Armed:
                sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, detailCode: null, nowUtc);
                events.Add(LocalExecutionSequenceEvents.StopRequested(sequence, nowUtc));
                events.Add(LocalExecutionSequenceEvents.Ended(sequence, nowUtc));
                break;

            case LocalExecutionSequenceState.WaitingForAdmission:
                {
                    // ADR-0062: the pending admission retires with the sequence in one commit; it owned nothing and claimed nothing.
                    var admission = await dbContext.LocalExecutionAdmissions.SingleOrDefaultAsync(
                        candidate => candidate.SequenceId == sequence.Id && candidate.State == LocalExecutionAdmissionState.Waiting,
                        cancellationToken);
                    if (admission is null)
                    {
                        return Failure(LocalExecutionSequenceErrors.PersistenceFailed());
                    }

                    await dbContext.Entry(admission).ReloadAsync(cancellationToken);
                    admission.Retire(LocalExecutionSequenceReason.StoppedBeforeClaim, nowUtc);
                    sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, detailCode: null, nowUtc);
                    events.Add(LocalExecutionSequenceEvents.StopRequested(sequence, nowUtc));
                    events.Add(LocalExecutionSequenceEvents.Ended(sequence, nowUtc));
                    break;
                }

            case LocalExecutionSequenceState.PreparingStep:
                {
                    // The claim did not commit first, so its preparation authority is revoked: nothing was claimed or consumed.
                    var step = await dbContext.LocalExecutionSequenceSteps.SingleAsync(
                        candidate => candidate.SequenceId == sequence.Id && candidate.State == LocalExecutionSequenceStepState.Reserved,
                        cancellationToken);
                    await dbContext.Entry(step).ReloadAsync(cancellationToken);

                    // ADR-0062: a queued reservation owns its workspace, so it retires only with its proved unused shape; anything
                    // uncertain stays held and the cycle needs attention instead of reporting a Stop it cannot prove.
                    var outcome = await LocalExecutionReservationRetirement.RetireOrHoldAsync(
                        dbContext, sequence, step, LocalExecutionSequenceReason.StoppedBeforeClaim,
                        LocalExecutionSequenceReason.PreparationUnresolved, nowUtc, cancellationToken);
                    switch (outcome)
                    {
                        case LocalExecutionReservationRetirement.Outcome.Held:
                            sequence.End(
                                LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.PreparationUnresolved, detailCode: null, nowUtc);
                            break;
                        default:
                            if (outcome == LocalExecutionReservationRetirement.Outcome.NotQueued)
                            {
                                step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, nowUtc);
                            }

                            sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, detailCode: null, nowUtc);
                            break;
                    }

                    events.Add(LocalExecutionSequenceEvents.StopRequested(sequence, nowUtc));
                    events.Add(LocalExecutionSequenceEvents.Ended(sequence, nowUtc));
                    break;
                }

            default:
                // The claim committed first: the already claimed operation may still dispatch and finish; no successor is allowed.
                sequence.BeginStopping(nowUtc);
                events.Add(LocalExecutionSequenceEvents.StopRequested(sequence, nowUtc));
                break;
        }

        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The revisions are the compare-and-set: a competing writer that committed after this decision read is never overwritten.
            return Failure(LocalExecutionSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(sequence.RunId, events[^1].Sequence, cancellationToken);
        return Success(sequence, changed: true);
    }

    private static Result<StopLocalExecutionSequenceCommandResult> Success(LocalExecutionSequence sequence, bool changed) =>
        Result<StopLocalExecutionSequenceCommandResult>.Success(new StopLocalExecutionSequenceCommandResult(
            sequence.Id, sequence.RunId, sequence.State, sequence.ReasonCode, sequence.Revision, changed));

    private static Result<StopLocalExecutionSequenceCommandResult> Failure(Error error) =>
        Result<StopLocalExecutionSequenceCommandResult>.Failure(error);

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
