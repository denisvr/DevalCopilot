using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one database-side reading of which waiting admissions of queued cycles can be admitted now (ADR-0062), decided only under the
/// write lock the caller already holds, from recorded rows, with no Git, provider or process observation. The waiting admissions are read
/// oldest-first by their Int64 number, bounded by the live-cycle limit (one more is read only to prove an overflow and then fails
/// closed), so no historical set is materialized and no unbounded set is sorted in memory. An admission is evaluated against the ordinary
/// workspace and capacity readings; an earlier eligible admission that competes for the same workspace or the next available mutating
/// unit is never bypassed, while a blocked workspace never blocks an independent project and a ReadOnly step needs no mutating unit.
/// Each verdict is a decision of one read, never a promise of a start time or a global position.
/// </summary>
internal static class LocalExecutionQueueEvaluator
{
    internal enum VerdictKind
    {
        /// <summary>Everything holds; the admission can be reserved.</summary>
        Eligible = 0,

        /// <summary>It waits, for the recorded fixed reason.</summary>
        Waiting = 1,

        /// <summary>Its deadline elapsed or its pinned authority changed: the cycle ends with attention.</summary>
        Ended = 2,
    }

    internal sealed record Verdict(VerdictKind Kind, string? WaitingReason, string? EndReason)
    {
        public static Verdict Eligible() => new(VerdictKind.Eligible, null, null);

        public static Verdict Waiting(string reason) => new(VerdictKind.Waiting, reason, null);

        public static Verdict Ended(string reason) => new(VerdictKind.Ended, null, reason);
    }

    internal sealed record Item(LocalExecutionAdmission Admission, LocalExecutionSequence Sequence, Run Run, Verdict Verdict);

    internal sealed record Evaluation(IReadOnlyList<Item> Items, bool Overflowed, int HeldPreparations);

    /// <summary>Evaluates every waiting admission, oldest first, with tracked entities freshly reloaded after the write lock was taken.
    /// <paramref name="workerAvailable"/> is an in-process observation that can only add a waiting reason, never bypass a database gate.</summary>
    public static async Task<Evaluation> EvaluateAsync(
        IDevalCopilotDbContext dbContext, DateTimeOffset nowUtc, bool workerAvailable, CancellationToken cancellationToken)
    {
        var limit = LocalExecutionSequencePolicy.MaximumQueuedCycles;
        var admissions = await dbContext.LocalExecutionAdmissions
            .Where(candidate => candidate.State == LocalExecutionAdmissionState.Waiting)
            .OrderBy(candidate => candidate.AdmissionNumber)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var overflowed = admissions.Count > limit;
        if (overflowed)
        {
            admissions = admissions.Take(limit).ToList();
        }

        var held = await LocalExecutionReservationOwnership.Held(dbContext).CountAsync(cancellationToken);
        var items = new List<Item>(admissions.Count);
        var claimedWorkspaces = new HashSet<Guid>();
        var claimedUnits = 0;
        var projectedHeld = held;
        var capacity = await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken);

        foreach (var admission in admissions)
        {
            await dbContext.Entry(admission).ReloadAsync(cancellationToken);
            var sequence = await dbContext.LocalExecutionSequences.SingleOrDefaultAsync(
                candidate => candidate.Id == admission.SequenceId, cancellationToken);
            if (sequence is null)
            {
                continue;
            }

            await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
            var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == sequence.RunId, cancellationToken);
            if (admission.State != LocalExecutionAdmissionState.Waiting
                || sequence.AdmissionMode != LocalExecutionAdmissionMode.WaitForAdmission
                || sequence.State != LocalExecutionSequenceState.WaitingForAdmission
                || run is null)
            {
                continue;
            }

            var verdict = await EvaluateOneAsync(
                dbContext, nowUtc, admission, sequence, run, capacity, claimedWorkspaces, claimedUnits, projectedHeld, workerAvailable,
                cancellationToken);
            if (verdict.Kind == VerdictKind.Eligible)
            {
                claimedWorkspaces.Add(sequence.WorkspaceId);
                projectedHeld++;
                if (LocalExecutionSequencePolicy.IsPotentiallyMutating(admission.Kind))
                {
                    claimedUnits++;
                }
            }

            items.Add(new Item(admission, sequence, run, verdict));
        }

        return new Evaluation(items, overflowed, held);
    }

    private static async Task<Verdict> EvaluateOneAsync(
        IDevalCopilotDbContext dbContext,
        DateTimeOffset nowUtc,
        LocalExecutionAdmission admission,
        LocalExecutionSequence sequence,
        Run run,
        ExecutionCapacityReading capacity,
        HashSet<Guid> claimedWorkspaces,
        int claimedUnits,
        int projectedHeld,
        bool workerAvailable,
        CancellationToken cancellationToken)
    {
        if (nowUtc >= sequence.AdvancementDeadlineUtc)
        {
            return Verdict.Ended(LocalExecutionSequenceReason.AdvancementDeadlineElapsed);
        }

        // The pinned authority and exact recorded source: a newer checkpoint, a changed recipe set or setting, a lease loss or a run that
        // is no longer a running manual run ends the cycle with attention. Busy ownership is not an authority change.
        if (!await LocalExecutionStepAuthority.SourceHoldsAsync(
                dbContext, sequence, run, admission.SourceCheckpointId, admission.SourceFingerprintSha256, cancellationToken))
        {
            return Verdict.Ended(LocalExecutionSequenceReason.AuthorityChanged);
        }

        var workspace = await WorkspaceExecutionPolicy.AssessAsync(
            dbContext, sequence.ProjectId, sequence.WorkspaceId, null, null, cancellationToken);
        switch (workspace.Status)
        {
            case WorkspaceExecutionStatus.Idle:
                break;
            case WorkspaceExecutionStatus.AgentAttemptActive:
            case WorkspaceExecutionStatus.VerificationActive:
            case WorkspaceExecutionStatus.CommitReserved:
            case WorkspaceExecutionStatus.PreparationReserved:
                return Verdict.Waiting(LocalExecutionWaitingReason.WaitingForWorkspace);
            default:
                // Conflicted, unavailable or missing ownership is never read as a future free slot.
                return Verdict.Waiting(LocalExecutionWaitingReason.Unavailable);
        }

        if (claimedWorkspaces.Contains(sequence.WorkspaceId))
        {
            return Verdict.Waiting(LocalExecutionWaitingReason.WaitingForEarlierEligibleWork);
        }

        if (LocalExecutionSequencePolicy.IsPotentiallyMutating(admission.Kind))
        {
            if (capacity is not { RecordedOwners: { } owners, Limit: { } limit } || capacity.Status == ExecutionCapacityStatus.Unavailable)
            {
                return Verdict.Waiting(LocalExecutionWaitingReason.Unavailable);
            }

            if (owners >= limit)
            {
                return Verdict.Waiting(LocalExecutionWaitingReason.WaitingForCapacity);
            }

            if (owners + claimedUnits >= limit)
            {
                return Verdict.Waiting(LocalExecutionWaitingReason.WaitingForEarlierEligibleWork);
            }
        }

        if (projectedHeld >= LocalExecutionSequencePolicy.MaximumHeldPreparations || !workerAvailable)
        {
            return Verdict.Waiting(LocalExecutionWaitingReason.WaitingForPreparationCapacity);
        }

        return Verdict.Eligible();
    }
}
