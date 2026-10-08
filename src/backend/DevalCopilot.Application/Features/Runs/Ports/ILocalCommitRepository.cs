namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// The mutation and proof side of one local commit (ADR-0029), always outside a database transaction and always with fixed
/// argument lists, a controlled environment, an owned proven-empty hooks directory, disabled signing and no network. Only the
/// recorded owned branch advances, by <c>update-ref</c> with the exact expected parent; only that worktree's administrative index
/// is synchronized, under an exclusively created owned lock and exact preimage checks. Working files are never written.
/// </summary>
public interface ILocalCommitRepository
{
    /// <summary>Exclusively acquires the administrative directory, existing index, bounded prepared artifact and a newly-created
    /// index lock. The returned facts describe live handles retained by this host; Application persists them before ref mutation.</summary>
    Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken);

    /// <summary>After a process loss, newly acquires a vacant index lock only for the exact already-promoted, still-preimage
    /// shape. It creates a fresh live capability; it never adopts an old lock or quarantine from a prior host.</summary>
    Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken);

    /// <summary>Performs the one expected-parent, non-dereferencing branch CAS while the persisted acquisition remains live.</summary>
    Task<LocalCommitExecutionResult> PromoteRefAsync(LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken);

    /// <summary>Performs the two no-replace index effects only after Application has durably recorded the quarantine plan.</summary>
    Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, string quarantineName, CancellationToken cancellationToken);

    /// <summary>Disposes the live acquisition after a definitely unpromoted ref result. The implementation may delete only its
    /// own still-held lock by handle; a false result leaves the operation for attention/recovery.</summary>
    Task<bool> ReleaseHeldIndexEffectsAsync(LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken);

    /// <summary>Promotes the recorded commit exactly once: verifies ownership, branch binding, parent and index preimage, moves
    /// the ref and synchronizes the index. Never retries a mutation.</summary>
    Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken);

    /// <summary>Read-only proof of the recorded commit, tree, parent, trailer, branch tip, index and lock facts.</summary>
    Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken);

    /// <summary>Finishes only an owned pending index promotion: the ref already holds the recorded commit, the index equals the
    /// recorded preimage and an owned or absent lock is proven. Anything else is refused as ambiguous.</summary>
    Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken);

    /// <summary>Removes only the prepared artifact and, when asked, the index lock this operation provably owns. Returns false
    /// when ownership is not proven and nothing was removed.</summary>
    Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken);
}
