using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

public sealed partial class LocalCommitGit
{
    private static LocalCommitExecutionResult NotPromoted(string reasonCode) =>
        new(LocalCommitExecutionOutcome.NotPromoted, reasonCode, false);

    private static LocalCommitExecutionResult Ambiguous(string reasonCode) =>
        new(LocalCommitExecutionOutcome.Ambiguous, reasonCode, false);

    public async Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        var acquisition = await AcquireIndexEffectsAsync(facts, cancellationToken);
        if (acquisition is null)
        {
            return NotPromoted("local_commit.index_acquisition_unproven");
        }

        var refResult = await PromoteRefAsync(facts, acquisition, cancellationToken);
        if (refResult.Outcome != LocalCommitExecutionOutcome.Promoted)
        {
            if (refResult.Outcome == LocalCommitExecutionOutcome.NotPromoted
                && !await ReleaseHeldIndexEffectsAsync(facts, acquisition, CancellationToken.None))
            {
                return Ambiguous("local_commit.lock_release_unproven");
            }

            return refResult;
        }

        // This convenience path exists only for direct adapter tests. Application persists the exact same deterministic name
        // before it calls PromoteHeldIndexAsync in production.
        return await PromoteHeldIndexAsync(
            facts, acquisition, $"devalcopilot-{facts.OperationId:N}.index-preimage", cancellationToken);
    }

    /// <summary>Compatibility entry point for callers built before receipt-backed recovery. A restart has no live lock handle,
    /// so this method deliberately refuses rather than recreating or adopting a pathname. Recovery uses a new acquisition and
    /// persists that receipt before calling <see cref="PromoteHeldIndexAsync"/>.</summary>
    public Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
        Task.FromResult(Ambiguous("local_commit.recovery_acquisition_required"));

    public async Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        var gitPath = ResolveGit();
        if (gitPath is null || !OperatingSystem.IsWindows())
        {
            return Unobserved(LocalCommitInspectionOutcome.GitUnavailable);
        }

        if (!storage.TryEnsureHooksDirectoryEmpty())
        {
            return Unobserved(LocalCommitInspectionOutcome.Unprovable);
        }

        var workspacePath = facts.WorkspacePath;
        var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, workspacePath, facts.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory
            || ownership.CommonDirectory is not { } commonDirectory)
        {
            return Unobserved(LocalCommitInspectionOutcome.Observed) with { OwnershipProven = false };
        }

        var bound = await HeadBoundToBranchAsync(gitPath, workspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(gitPath, workspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        if (bound is null || !tip.Ok)
        {
            return Unobserved(LocalCommitInspectionOutcome.Unprovable);
        }

        var objectState = await CommitObjectStateAsync(gitPath, workspacePath, facts, cancellationToken, administrativeDirectory);
        var indexPath = Path.Combine(administrativeDirectory, "index");
        var indexSha = IndexSha256(indexPath);
        var indexState = indexSha is null ? LocalCommitIndexState.Missing
            : indexSha == facts.IndexPreimageSha256 ? LocalCommitIndexState.Preimage
            : indexSha == facts.PreparedIndexSha256 ? LocalCommitIndexState.Prepared
            : LocalCommitIndexState.Other;

        var lockPath = indexPath + ".lock";
        // A pathname and matching bytes are not an operation identity.  A lock may be written by another Git process with the
        // same proposed index, or replaced after an earlier observation.  Recovery therefore never adopts a pre-existing lock;
        // only the live execution that just created its exclusive lock may promote it.
        var lockState = File.Exists(lockPath) ? LocalCommitLockState.Unknown : LocalCommitLockState.None;

        var artifactPath = storage.ResolveArtifact(facts.PreparedIndexRelativePath);
        var artifactIntact = artifactPath is not null
            && string.Equals(FileSha256(artifactPath), facts.PreparedIndexSha256, StringComparison.Ordinal);

        var sourceConsistent = false;
        if (tip.Ok && LocalCommitOperation.IsObjectId(tip.Value) && indexSha is not null)
        {
            var observation = await ObserveControlledAsync(
                gitPath, workspacePath, commonDirectory, tip.Value!, indexPath, facts.OperationId, cancellationToken);
            sourceConsistent = observation.Proven && observation.Clean;
        }

        // Read last, after every other observation: a lock that is held at this instant may belong to an update that began after the
        // values above were read, and only a positively clear, redirection-free namespace lets those values decide anything.
        var referenceLocks = LocalCommitReferenceLocks.Probe(commonDirectory, administrativeDirectory, facts.BranchName);

        return new LocalCommitInspection(
            LocalCommitInspectionOutcome.Observed,
            true,
            bound == true,
            tip.Value,
            objectState,
            indexState,
            lockState,
            artifactIntact,
            sourceConsistent,
            referenceLocks);
    }

    public async Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken)
    {
        if (removeOwnedLock)
        {
            var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
            if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory)
            {
                return false;
            }

            var lockPath = Path.Combine(administrativeDirectory, "index.lock");
            if (File.Exists(lockPath) && !TryRemoveOwnedLock(lockPath, facts))
            {
                return false;
            }
        }

        TryDeleteDirectory(storage.OperationDirectory(facts.OperationId));
        return true;
    }

    private static LocalCommitInspection Unobserved(LocalCommitInspectionOutcome outcome) => new(
        outcome, false, false, null, LocalCommitObjectState.Absent, LocalCommitIndexState.Missing, LocalCommitLockState.None, false, false);
    /// <summary>A recovery path has no retained physical handle for an existing pathname. It must never turn a matching content
    /// hash into ownership: another process can create the same bytes or replace the file between path operations. The current
    /// implementation therefore refuses to remove every extant lock and leaves it for explicit operator intervention.</summary>
    private static bool TryRemoveOwnedLock(string lockPath, LocalCommitFacts facts)
    {
        try
        {
            return !File.Exists(lockPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
