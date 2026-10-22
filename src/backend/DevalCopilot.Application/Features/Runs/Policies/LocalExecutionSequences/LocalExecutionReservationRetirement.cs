using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The only way an unused queued reservation leaves ownership (ADR-0062): called under the write lock the caller holds, on a stop, a
/// preparation expiry, a startup closure or a settled failed preparation. The step and its Reserved admission are retired together, and
/// the old preparation token is fenced in the same commit because the step is no longer Reserved, only after the full reservation and step
/// identity, the Reserved/unlinked shape, the complete historical run, project, workspace, checkpoint and fingerprint identity (the same predicate the ownership reading counts) and the absence of the reserved operation in both operation families were proved from locked
/// rows. A reservation that cannot be proved unused (a contradictory row, a present operation, a foreign or malformed link) stays held:
/// step and admission become Unresolved, still counted as owners, with attention instead of a release. A claimed or dispatched
/// operation is never touched here, and a preceding independent probe alone never authorizes retirement.
/// </summary>
internal static class LocalExecutionReservationRetirement
{
    internal enum Outcome
    {
        /// <summary>An Immediate sequence's step: the caller keeps its ADR-0060 behavior; nothing here was changed.</summary>
        NotQueued = 0,

        /// <summary>The proved unused reservation was retired: the step is Revoked and the admission Retired.</summary>
        Retired = 1,

        /// <summary>The reservation could not be proved unused and stays held: the step and admission are Unresolved.</summary>
        Held = 2,
    }

    /// <summary>An unproved preparation (an unanswered or contradictory independent probe): the step and a queued Reserved admission
    /// become Unresolved and stay counted as owners. Nothing is retired, retried or released.</summary>
    public static async Task HoldAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        string holdReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        step.MarkUnresolved(holdReason, nowUtc);
        if (sequence.AdmissionMode != LocalExecutionAdmissionMode.WaitForAdmission)
        {
            return;
        }

        var admission = await dbContext.LocalExecutionAdmissions.SingleOrDefaultAsync(
            candidate => candidate.StepId == step.Id, cancellationToken);
        if (admission is null)
        {
            return;
        }

        await dbContext.Entry(admission).ReloadAsync(cancellationToken);
        if (admission.State == LocalExecutionAdmissionState.Reserved)
        {
            admission.MarkUnresolved(holdReason, nowUtc);
        }
    }

    public static async Task<Outcome> RetireOrHoldAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        string retireReason,
        string holdReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (sequence.AdmissionMode != LocalExecutionAdmissionMode.WaitForAdmission)
        {
            return Outcome.NotQueued;
        }

        var admission = await dbContext.LocalExecutionAdmissions.SingleOrDefaultAsync(
            candidate => candidate.StepId == step.Id, cancellationToken);
        if (admission is not null)
        {
            await dbContext.Entry(admission).ReloadAsync(cancellationToken);
        }

        var proved = admission is not null
            && step.State == LocalExecutionSequenceStepState.Reserved
            && step.OperationId is null
            && step.ClaimedAtUtc is null
            && LocalExecutionStepAuthority.IsReservedFor(admission, sequence, step)
            && LocalExecutionReservationOwnership.KindMappingHolds(step.Kind)
            && await LocalExecutionReservationOwnership.IdentityHoldsAsync(dbContext, admission, cancellationToken)
            && !await dbContext.Attempts.AsNoTracking().AnyAsync(candidate => candidate.Id == step.ReservedOperationId, cancellationToken)
            && !await dbContext.VerificationExecutions.AsNoTracking().AnyAsync(candidate => candidate.Id == step.ReservedOperationId, cancellationToken);
        if (proved)
        {
            step.Revoke(retireReason, nowUtc);
            admission!.Retire(retireReason, nowUtc);
            return Outcome.Retired;
        }

        if (step.State == LocalExecutionSequenceStepState.Reserved)
        {
            step.MarkUnresolved(holdReason, nowUtc);
        }

        if (admission is { State: LocalExecutionAdmissionState.Reserved })
        {
            admission.MarkUnresolved(holdReason, nowUtc);
        }

        return Outcome.Held;
    }
}
