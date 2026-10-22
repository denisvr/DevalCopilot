using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ReserveLocalExecutionAdmission;

/// <summary>
/// The queue's one reservation decision (ADR-0062). The transaction's first statement writes a run row, which takes the database write
/// lock, so a Stop, a claim, an expiry, a competing manual entrance or a restart that committed first is read here and one that arrives
/// later contends on the revisions and on the ownership this reservation then creates. Every decision is a function of locked durable
/// state and the clock: the oldest eligible admission by its database number is chosen, so a blocked workspace never blocks an
/// independent project and an older eligible contender is never bypassed. Reserving creates the ordinary Reserved step with its
/// original-host preparation token and marks the admission Reserved in one commit; nothing external runs and nothing is claimed.
/// </summary>
public sealed class ReserveLocalExecutionAdmissionCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ReserveLocalExecutionAdmissionCommand, Result<ReserveLocalExecutionAdmissionCommandResult>>
{
    public async Task<Result<ReserveLocalExecutionAdmissionCommandResult>> HandleAsync(
        ReserveLocalExecutionAdmissionCommand command, CancellationToken cancellationToken)
    {
        var oldestRun = await dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(candidate => candidate.State == LocalExecutionAdmissionState.Waiting)
            .OrderBy(candidate => candidate.AdmissionNumber)
            .Select(candidate => dbContext.LocalExecutionSequences.Where(sequence => sequence.Id == candidate.SequenceId)
                .Select(sequence => (Guid?)sequence.RunId).FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken);
        if (oldestRun is not { } lockRun)
        {
            return None();
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == lockRun)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return None();
        }

        var nowUtc = timeProvider.GetUtcNow();
        var evaluation = await LocalExecutionQueueEvaluator.EvaluateAsync(dbContext, nowUtc, workerAvailable: true, cancellationToken);
        var chosen = evaluation.Overflowed || evaluation.HeldPreparations >= LocalExecutionSequencePolicy.MaximumHeldPreparations
            ? null
            : evaluation.Items.FirstOrDefault(item => item.Verdict.Kind == LocalExecutionQueueEvaluator.VerdictKind.Eligible);
        if (chosen is null)
        {
            return None();
        }

        var admission = chosen.Admission;
        var sequence = chosen.Sequence;
        var step = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(),
            sequence.Id,
            admission.Ordinal,
            admission.Kind,
            admission.PassNumber,
            admission.RecipeOrdinal,
            admission.PredecessorStepId,
            admission.SourceCheckpointId,
            admission.SourceFingerprintSha256,
            admission.ReportMessageId,
            admission.SubjectAttemptId,
            reservedOperationId: Guid.NewGuid(),
            preparationOwnerToken: Guid.NewGuid(),
            nowUtc);
        dbContext.LocalExecutionSequenceSteps.Add(step);
        admission.Reserve(step.Id, nowUtc);
        sequence.BeginPreparing(nowUtc);
        var reserved = LocalExecutionSequenceEvents.StepReserved(sequence, step, nowUtc);
        dbContext.Events.Add(reserved);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The revisions and the database guards are the compare-and-set: a competing writer that committed after this decision read
            // is never overwritten, and no ticket is handed out for a reservation that did not commit.
            return Result<ReserveLocalExecutionAdmissionCommandResult>.Failure(LocalExecutionSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(sequence.RunId, reserved.Sequence, cancellationToken);
        var ticket = await LocalExecutionStepTicketFactory.CreateAsync(dbContext, step, cancellationToken);
        return Result<ReserveLocalExecutionAdmissionCommandResult>.Success(
            new ReserveLocalExecutionAdmissionCommandResult(sequence.RunId, ticket));
    }

    private static Result<ReserveLocalExecutionAdmissionCommandResult> None() =>
        Result<ReserveLocalExecutionAdmissionCommandResult>.Success(new ReserveLocalExecutionAdmissionCommandResult(null, null));

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
