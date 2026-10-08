using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

public enum LocalCommitRecoveryAction
{
    /// <summary>The branch, index and locks prove the operation never promoted: record Interrupted.</summary>
    Interrupt,

    /// <summary>The recorded commit, branch and index are proven: record Completed.</summary>
    Complete,

    /// <summary>The ref holds the recorded commit and an owned index promotion is still pending: finish it, then complete.</summary>
    FinishPromotionThenComplete,

    /// <summary>Nothing may be decided; the operation stays reserved or NeedsAttention.</summary>
    Attention,
}

/// <summary>What recovery decided and why. <paramref name="RemoveOwnedLock"/> is set only when the lock's bytes prove this
/// operation created it.</summary>
public sealed record LocalCommitRecoveryDecision(LocalCommitRecoveryAction Action, string ReasonCode, bool RemoveOwnedLock);

/// <summary>
/// Decides from recorded facts and an exact inspection, never from an exit code, a reflog or a search for a plausible commit
/// (ADR-0029). Every state that is not exactly one of the proven shapes is Attention.
/// </summary>
public static class LocalCommitRecoveryPolicy
{
    public static LocalCommitRecoveryDecision Decide(LocalCommitOperation operation, LocalCommitInspection inspection)
    {
        if (operation.IsTerminal)
        {
            return Attention("local_commit.already_terminal");
        }

        if (inspection.Outcome != LocalCommitInspectionOutcome.Observed)
        {
            return Attention("local_commit.git_unprovable");
        }

        if (!inspection.OwnershipProven)
        {
            return Attention("local_commit.ownership_unprovable");
        }

        if (!inspection.HeadBoundToBranch || inspection.BranchTipSha is null)
        {
            return Attention("local_commit.head_not_bound");
        }

        // A recovery effect or a terminal decision needs positive evidence that no other process holds the owned reference or HEAD lock:
        // such a lock may belong to an update that is still in flight, so neither release, completion nor a recorded index promotion
        // may rest on the observed values alone. Missing or unreadable proof is unknown, and a lock is never adopted or removed.
        if (inspection.ReferenceLocks != LocalCommitReferenceLockState.Clear)
        {
            return Attention(
                inspection.ReferenceLocks == LocalCommitReferenceLockState.Present
                    ? "local_commit.unknown_reference_lock"
                    : "local_commit.reference_lock_unproven");
        }

        var atParent = string.Equals(inspection.BranchTipSha, operation.ParentCommitSha, StringComparison.Ordinal);
        var atCommit = string.Equals(inspection.BranchTipSha, operation.CommitSha, StringComparison.Ordinal);
        if (operation.Status == LocalCommitStatus.Prepared)
        {
            return atParent && inspection.Index == LocalCommitIndexState.Preimage && inspection.IndexLock == LocalCommitLockState.None
                ? new LocalCommitRecoveryDecision(LocalCommitRecoveryAction.Interrupt, "local_commit.interrupted_before_execution", false)
                : Attention("local_commit.prepared_state_unexpected");
        }

        if (atParent)
        {
            return inspection.Index == LocalCommitIndexState.Preimage && inspection.IndexLock == LocalCommitLockState.None
                    ? new LocalCommitRecoveryDecision(LocalCommitRecoveryAction.Interrupt, "local_commit.interrupted_unpromoted", false)
                    : Attention("local_commit.unpromoted_state_unknown");
        }

        if (!atCommit || inspection.CommitObject != LocalCommitObjectState.ExactMatch)
        {
            return Attention("local_commit.branch_tip_unrecognized");
        }

        if (inspection.IndexLock == LocalCommitLockState.Unknown)
        {
            return Attention("local_commit.unknown_index_lock");
        }

        if (inspection.Index == LocalCommitIndexState.Prepared && inspection.IndexLock == LocalCommitLockState.None)
        {
            // The recorded commit, tree, parent, trailer, branch tip, binding, ownership and index are all exact, so the delivery
            // is proven. Whether the working files still equal the delivered tree only decides the workspace label (the recorder
            // keeps it under attention otherwise); an edit after the last observation never changes or un-delivers the commit.
            return new LocalCommitRecoveryDecision(LocalCommitRecoveryAction.Complete, "local_commit.recovered_promoted", false);
        }

        return inspection.Index switch
        {
            LocalCommitIndexState.Preimage when operation.Status == LocalCommitStatus.Executing
                && operation.IndexAcquiredAtUtc is not null && operation.IndexReplacementPlannedAtUtc is not null
                && !string.IsNullOrWhiteSpace(operation.IndexQuarantineName) && inspection.PreparedArtifactIntact =>
                new LocalCommitRecoveryDecision(
                    LocalCommitRecoveryAction.FinishPromotionThenComplete, "local_commit.recovered_index_finished", false),
            _ => Attention("local_commit.index_state_unrecognized"),
        };
    }

    private static LocalCommitRecoveryDecision Attention(string reasonCode) =>
        new(LocalCommitRecoveryAction.Attention, reasonCode, false);
}
