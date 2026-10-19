using System.Text;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;

/// <summary>
/// The coordinator's one decision step (ADR-0055). The transaction's first statement writes the sequence row, which takes the database
/// write lock, so a Stop, a claim, a classification or a restart that committed first is read here and one that arrives later
/// contends on the revisions this save advances. Every decision is a function of locked durable state and the clock: nothing is
/// inferred from an earlier tick, the loaded UI, a latest message or process knowledge.
/// </summary>
public sealed class ObservePlanningSequenceCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ObservePlanningSequenceCommand, Result<ObservePlanningSequenceCommandResult>>
{
    public async Task<Result<ObservePlanningSequenceCommandResult>> HandleAsync(
        ObservePlanningSequenceCommand command, CancellationToken cancellationToken)
    {
        var known = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.Id == command.SequenceId)
            .Select(candidate => new { candidate.State, candidate.RunId })
            .SingleOrDefaultAsync(cancellationToken);
        if (known is null)
        {
            return Failure(PlanningSequenceErrors.NotFound());
        }

        var knownState = known.State;

        if (!PlanningSequencePolicy.IsAuthorityLive(knownState))
        {
            return Success(known.RunId, knownState, changed: false, ticket: null);
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == known.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(PlanningSequenceErrors.NotFound());
        }

        var sequence = await dbContext.PlanningSequences.SingleAsync(candidate => candidate.Id == command.SequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        if (!sequence.IsAuthorityLive)
        {
            return Success(sequence.RunId, sequence.State, changed: false, ticket: null);
        }

        var steps = await dbContext.PlanningSequenceSteps
            .Where(candidate => candidate.SequenceId == sequence.Id)
            .OrderBy(candidate => candidate.Ordinal)
            .ToListAsync(cancellationToken);
        foreach (var step in steps)
        {
            await dbContext.Entry(step).ReloadAsync(cancellationToken);
        }

        var run = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == sequence.RunId, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        var events = new List<RunEvent>();
        PlanningSequenceStepTicket? ticket = null;

        switch (sequence.State)
        {
            case PlanningSequenceState.Armed:
                if (steps.Count != 0 || !await PlanningSequencePinnedSource.HoldsAsync(dbContext, sequence, run, cancellationToken))
                {
                    End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.AuthorityChanged, null, nowUtc);
                    break;
                }

                ticket = Reserve(sequence, ordinal: 0, predecessorAttemptId: null, inputProposalMessageId: null, nowUtc, events);
                break;

            case PlanningSequenceState.PreparingStep:
                ObservePreparation(sequence, steps, events, nowUtc);
                break;

            default:
                ticket = await ObserveClaimedStepAsync(sequence, steps, run, events, nowUtc, cancellationToken);
                break;
        }

        if (events.Count == 0)
        {
            return Success(sequence.RunId, sequence.State, changed: false, ticket: null);
        }

        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The revisions are the compare-and-set: a competing writer that committed after this decision read is never overwritten,
            // and no ticket is handed out for a reservation that did not commit.
            return Failure(PlanningSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(sequence.RunId, events[^1].Sequence, cancellationToken);
        return Success(sequence.RunId, sequence.State, changed: true, ticket);
    }

    /// <summary>A reserved step whose two-minute preparation deadline elapsed: its authority is revoked. The claim commits only with
    /// the step still Reserved, so nothing was claimed or consumed.</summary>
    private static void ObservePreparation(
        PlanningSequence sequence, IReadOnlyList<PlanningSequenceStep> steps, List<RunEvent> events, DateTimeOffset nowUtc)
    {
        var reserved = steps.SingleOrDefault(candidate => candidate.State == PlanningSequenceStepState.Reserved);
        if (reserved is null)
        {
            End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.StepOutcomeInvalid, null, nowUtc);
            return;
        }

        if (nowUtc < reserved.PreparationDeadlineUtc)
        {
            return;
        }

        reserved.Revoke(PlanningSequenceReason.PreparationExpired, nowUtc);
        End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.PreparationExpired, null, nowUtc);
    }

    /// <summary>AwaitingStepResult or Stopping: the claimed step's attempt is observed, never fabricated or terminated.</summary>
    private async Task<PlanningSequenceStepTicket?> ObserveClaimedStepAsync(
        PlanningSequence sequence,
        IReadOnlyList<PlanningSequenceStep> steps,
        Run run,
        List<RunEvent> events,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var claimed = steps.SingleOrDefault(candidate => candidate.State == PlanningSequenceStepState.Claimed);
        var attempt = claimed?.AttemptId is { } attemptId
            ? await dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == attemptId, cancellationToken)
            : null;
        if (claimed is null || attempt is null || attempt.RunId != sequence.RunId)
        {
            End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.StepOutcomeInvalid, null, nowUtc);
            return null;
        }

        if (attempt.Status == AttemptStatus.Running)
        {
            // The result deadline is a coordination observation, not measured invocation time or proof of process death. Expiry closes
            // advancement; it never marks the attempt terminal, cancels its process or releases its ownership.
            var deadline = PlanningSequencePolicy.ResultDeadlineOf(attempt.ClaimedAtUtc, attempt.AgentTimeout);
            if (deadline is null || nowUtc >= deadline)
            {
                End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed, null, nowUtc);
            }

            return null;
        }

        // The attempt is terminal. A late result can finish its own attempt but never revives or advances a closed sequence, and a
        // Stop that won before classification keeps Stopped even if the provider produced a valid acceptance.
        claimed.Conclude(nowUtc);
        events.Add(PlanningSequenceEvents.StepConcluded(sequence, claimed, attempt.Status, attempt.AgentOutcome, nowUtc));

        if (sequence.State == PlanningSequenceState.Stopping)
        {
            End(sequence, events, PlanningSequenceState.Stopped, PlanningSequenceReason.StoppedAfterStep, null, nowUtc);
            return null;
        }

        if (attempt.Status != AttemptStatus.Completed)
        {
            End(
                sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.StepFailed,
                ToDetail(attempt.AgentOutcome), nowUtc);
            return null;
        }

        // A result completed at or after the deadline never authorizes a successor or successful planning, even when the sweep had not
        // yet run. The terminal attempt and its outputs stay as recorded; a timely completion that is observed later is not late.
        if (PlanningSequencePolicy.IsResultLate(attempt.ClaimedAtUtc, attempt.AgentTimeout, attempt.CompletedAtUtc ?? nowUtc))
        {
            End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed, null, nowUtc);
            return null;
        }

        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, sequence.RunId, cancellationToken);
        var verdict = snapshot is null
            ? PlanningSequenceVerdict.Invalid()
            : PlanningSequenceOutcomeClassifier.Classify(snapshot, sequence, claimed, attempt);

        switch (verdict.Kind)
        {
            case PlanningSequenceVerdictKind.Accepted:
                End(sequence, events, PlanningSequenceState.ReadyForImplementation, PlanningSequenceReason.PlanAccepted, null, nowUtc);
                return null;

            case PlanningSequenceVerdictKind.Escalated:
                End(sequence, events, PlanningSequenceState.AwaitingHumanDecision, PlanningSequenceReason.SecondRoundEscalated, null, nowUtc);
                return null;

            case PlanningSequenceVerdictKind.Continue when claimed.Ordinal + 1 < PlanningSequencePolicy.MaximumSteps:
                if (!await PlanningSequencePinnedSource.HoldsAsync(dbContext, sequence, run, cancellationToken))
                {
                    End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.AuthorityChanged, null, nowUtc);
                    return null;
                }

                return Reserve(
                    sequence, claimed.Ordinal + 1, verdict.PredecessorAttemptId, verdict.InputProposalMessageId, nowUtc, events);

            default:
                End(sequence, events, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.StepOutcomeInvalid, null, nowUtc);
                return null;
        }
    }

    private PlanningSequenceStepTicket Reserve(
        PlanningSequence sequence,
        int ordinal,
        Guid? predecessorAttemptId,
        Guid? inputProposalMessageId,
        DateTimeOffset nowUtc,
        List<RunEvent> events)
    {
        var step = PlanningSequenceStep.Reserve(
            Guid.NewGuid(),
            sequence.Id,
            ordinal,
            predecessorAttemptId,
            inputProposalMessageId,
            reservedAttemptId: Guid.NewGuid(),
            preparationOwnerToken: Guid.NewGuid(),
            nowUtc);
        dbContext.PlanningSequenceSteps.Add(step);
        sequence.BeginPreparing(nowUtc);
        events.Add(PlanningSequenceEvents.StepReserved(sequence, step, nowUtc));
        return new PlanningSequenceStepTicket(
            sequence.Id, step.Id, ordinal, step.ReservedAttemptId, step.PreparationOwnerToken, predecessorAttemptId, inputProposalMessageId);
    }

    private static void End(
        PlanningSequence sequence,
        List<RunEvent> events,
        PlanningSequenceState endState,
        string reasonCode,
        string? detailCode,
        DateTimeOffset nowUtc)
    {
        sequence.End(endState, reasonCode, detailCode, nowUtc);
        events.Add(PlanningSequenceEvents.Ended(sequence, nowUtc));
    }

    /// <summary>The closed outcome name as a bounded lowercase detail code (for example <c>planning_sequence_dispatch_refused</c>).</summary>
    private static string? ToDetail(AgentOutcome? outcome)
    {
        if (outcome is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var character in outcome.Value.ToString())
        {
            if (char.IsUpper(character) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static Result<ObservePlanningSequenceCommandResult> Success(
        Guid runId, PlanningSequenceState state, bool changed, PlanningSequenceStepTicket? ticket) =>
        Result<ObservePlanningSequenceCommandResult>.Success(new ObservePlanningSequenceCommandResult(runId, state, changed, ticket));

    private static Result<ObservePlanningSequenceCommandResult> Failure(Error error) =>
        Result<ObservePlanningSequenceCommandResult>.Failure(error);

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
