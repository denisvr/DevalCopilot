using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

public sealed partial class LocalCommitGit
{
    public async Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || ResolveGit() is not { } gitPath || !storage.TryEnsureHooksDirectoryEmpty())
        {
            return null;
        }

        var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory
            || !await IsExactPreMutationStateAsync(gitPath, facts, administrativeDirectory, cancellationToken))
        {
            return null;
        }

        return await AcquireVerifiedIndexEffectsAsync(facts, administrativeDirectory, cancellationToken);
    }

    public async Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(
        LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !WindowsIndexEffectHandles.IsPlainName(quarantineName)
            || ResolveGit() is not { } gitPath || !storage.TryEnsureHooksDirectoryEmpty())
        {
            return null;
        }

        var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory
            || File.Exists(Path.Combine(administrativeDirectory, quarantineName))
            || File.Exists(Path.Combine(administrativeDirectory, "index.lock")))
        {
            return null;
        }

        var bound = await HeadBoundToBranchAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        if (bound != true || !tip.Ok || !string.Equals(tip.Value, facts.CommitSha, StringComparison.Ordinal)
            || await CommitObjectStateAsync(gitPath, facts.WorkspacePath, facts, cancellationToken, administrativeDirectory)
                != LocalCommitObjectState.ExactMatch
            || !string.Equals(IndexSha256(Path.Combine(administrativeDirectory, "index")), facts.IndexPreimageSha256, StringComparison.Ordinal))
        {
            return null;
        }

        return await AcquireVerifiedIndexEffectsAsync(facts, administrativeDirectory, cancellationToken);
    }

    private async Task<LocalCommitIndexAcquisition?> AcquireVerifiedIndexEffectsAsync(
        LocalCommitFacts facts, string provenAdministrativeDirectory, CancellationToken cancellationToken)
    {
        // The ownership proof is repeated immediately before the physical acquisition and must name the same directory.
        var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
        var artifactPath = storage.ResolveArtifact(facts.PreparedIndexRelativePath);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory || artifactPath is null
            || !SameDirectory(administrativeDirectory, provenAdministrativeDirectory))
        {
            return null;
        }

        if (!WindowsIndexEffectHandles.TryAcquire(
                administrativeDirectory, artifactPath, facts.IndexPreimageSha256, facts.PreparedIndexSha256, facts.BranchName,
                out var handles, out var receipt) || handles is null || receipt is null)
        {
            return null;
        }

        if (!heldIndexEffects.TryAdd(facts.OperationId, handles))
        {
            handles.Dispose();
            return null;
        }

        return new LocalCommitIndexAcquisition(
            receipt.AdministrativeDirectoryIdentity,
            receipt.PreimageIdentity,
            receipt.PreimageLength,
            receipt.PreparedArtifactIdentity,
            receipt.PreparedArtifactLength,
            receipt.LockIdentity,
            receipt.LockLength,
            receipt.LockName);
    }

    public async Task<LocalCommitExecutionResult> PromoteRefAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        if (!TryGetHeld(facts.OperationId, acquisition, out var handles) || ResolveGit() is not { } gitPath)
        {
            return Ambiguous("local_commit.index_acquisition_unproven");
        }

        var ownership = await ProveOwnershipAsync(
            facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory)
        {
            return Ambiguous("local_commit.ownership_changed_before_ref_cas");
        }

        if (!handles!.Verify() || !handles.IsHeadBoundTo(facts.BranchName))
        {
            return Ambiguous("local_commit.head_binding_unproven");
        }

        if (!await IsExactPreMutationStateAsync(gitPath, facts, administrativeDirectory, cancellationToken))
        {
            // Nothing was started, so the mutation was never attempted; the caller still proves safe release before it records
            // a failure, because the reason this check failed may be an external change.
            return NotPromoted("local_commit.pre_mutation_state_changed");
        }

        RefTransactionObserver?.Invoke("before_start");
        var start = await LocalCommitRefTransaction.StartAsync(
            LocalCommitRefTransaction.CreateGitStartInfo(
                gitPath, facts.WorkspacePath, administrativeDirectory, storage.EmptyConfigPath, storage.HooksDirectory),
            facts.BranchName, facts.CommitSha, facts.ParentCommitSha, RefTransactionBudgets, RefTransactionObserver, cancellationToken);
        if (start.Transaction is not { } transaction)
        {
            if (cancellationToken.IsCancellationRequested && start.NoMutationProven)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var reason = "local_commit.ref_transaction_" + ReasonWord(start.Reason);
            return start.NoMutationProven ? NotPromoted(reason) : Ambiguous(reason);
        }

        await using var owner = transaction;
        var proven = false;
        try
        {
            proven = await transaction.RunProofAsync(
                token => ProveUnderPreparedLockAsync(facts, acquisition, gitPath, administrativeDirectory, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A caller shutting down aborts the prepared lock under the owner's own cleanup budget and then propagates, so the
            // durable marker stays as written for startup recovery.
            await transaction.AbortAsync();
            throw;
        }
        catch (Exception)
        {
            proven = false;
        }

        if (!proven)
        {
            return await transaction.AbortAsync() == LocalCommitRefAbortOutcome.Confirmed
                ? NotPromoted("local_commit.ref_transaction_aborted")
                : Ambiguous("local_commit.ref_transaction_abort_unproven");
        }

        var committed = await transaction.CommitAsync(cancellationToken);
        if (committed == LocalCommitRefCommitOutcome.NotAttempted)
        {
            return await transaction.AbortAsync() == LocalCommitRefAbortOutcome.Confirmed
                ? NotPromoted("local_commit.ref_transaction_aborted_before_commit")
                : Ambiguous("local_commit.ref_transaction_abort_unproven");
        }

        if (committed != LocalCommitRefCommitOutcome.Acknowledged)
        {
            return Ambiguous("local_commit.ref_transaction_commit_" + ReasonWord(transaction.FailureReason));
        }

        // The acknowledgement is evidence, not proof: the post-effect state is read again from the proven context.
        var bound = await HeadBoundToBranchAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        if (bound != true || !tip.Ok || !handles.IsHeadBoundTo(facts.BranchName))
        {
            return Ambiguous("local_commit.binding_unprovable");
        }

        if (string.Equals(tip.Value, facts.ParentCommitSha, StringComparison.Ordinal))
        {
            return NotPromoted("local_commit.ref_unchanged");
        }

        return string.Equals(tip.Value, facts.CommitSha, StringComparison.Ordinal)
            && await IsDirectBranchAsync(gitPath, facts, administrativeDirectory, cancellationToken)
            && TryGetHeld(facts.OperationId, acquisition, out _)
                ? new LocalCommitExecutionResult(LocalCommitExecutionOutcome.Promoted, "local_commit.ref_promoted", false)
                : Ambiguous("local_commit.branch_tip_unrecognized");
    }

    /// <summary>Every proof the prepared Git lock must still see: the ownership and administrative directory, the live physical
    /// handles including HEAD, a direct (non-symbolic) owned ref, and the exact parent, binding and commit object. All reads use
    /// the explicitly proven Git directory and the transaction's own scope.</summary>
    private async Task<bool> ProveUnderPreparedLockAsync(
        LocalCommitFacts facts,
        LocalCommitIndexAcquisition acquisition,
        string gitPath,
        string administrativeDirectory,
        CancellationToken token)
    {
        var ownership = await ProveOwnershipAsync(facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, token);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } proven
            || !SameDirectory(proven, administrativeDirectory))
        {
            return ProofFault("ownership");
        }

        if (!TryGetHeld(facts.OperationId, acquisition, out var handles) || !handles!.Verify())
        {
            return ProofFault("handles");
        }

        if (!handles.IsHeadBoundTo(facts.BranchName))
        {
            return ProofFault("head_literal_binding");
        }

        // A redirected owned ref can resolve to the very parent object id, so neither an expected-parent compare-and-swap nor a
        // resolved HEAD read alone distinguishes it: the ref's own type is proven first, under the prepared lock.
        if (!await IsDirectBranchAsync(gitPath, facts, administrativeDirectory, token))
        {
            return ProofFault("ref_not_direct");
        }

        return await IsExactPreMutationStateAsync(gitPath, facts, administrativeDirectory, token) || ProofFault("pre_mutation_state");
    }

    /// <summary>Names the first failed prepared-lock proof through the test observation seam and fails it.</summary>
    private bool ProofFault(string label)
    {
        RefTransactionObserver?.Invoke("proof_fault:" + label);
        return false;
    }

    /// <summary>True only when the owned ref is a direct reference. <c>symbolic-ref -q</c> exits 1 for a non-symbolic ref and 0
    /// (printing the target) for a redirect; any other answer is unproven.</summary>
    private async Task<bool> IsDirectBranchAsync(
        string gitPath, LocalCommitFacts facts, string administrativeDirectory, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            gitPath, facts.WorkspacePath, ["symbolic-ref", "-q", "refs/heads/" + facts.BranchName], cancellationToken,
            provenGitDirectory: administrativeDirectory);
        return result.Outcome == LocalCommitGitOutcome.Exited && result.ExitCode == 1 && !result.Truncated;
    }

    public async Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
        LocalCommitFacts facts,
        LocalCommitIndexAcquisition acquisition,
        string quarantineName,
        CancellationToken cancellationToken)
    {
        if (!TryGetHeld(facts.OperationId, acquisition, out var handles) || ResolveGit() is not { } gitPath)
        {
            return Ambiguous("local_commit.index_acquisition_unproven");
        }

        var ownership = await ProveOwnershipAsync(
            facts.MainRepositoryPath, facts.WorkspacePath, facts.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory
            || ownership.CommonDirectory is not { } commonDirectory)
        {
            return Ambiguous("local_commit.pending_promotion_unproven");
        }

        var bound = await HeadBoundToBranchAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        if (bound != true || !tip.Ok || !string.Equals(tip.Value, facts.CommitSha, StringComparison.Ordinal)
            || !handles!.IsHeadBoundTo(facts.BranchName)
            || await CommitObjectStateAsync(gitPath, facts.WorkspacePath, facts, cancellationToken, administrativeDirectory)
                != LocalCommitObjectState.ExactMatch)
        {
            return Ambiguous("local_commit.pending_promotion_unproven");
        }

        if (!handles.TryPromote(quarantineName))
        {
            // A failed native phase can mean either a preserved foreign destination or an interruption after the first durable
            // rename. Keep the live capabilities reserved; neither recovery nor cleanup may recreate or infer them by pathname.
            return Ambiguous($"local_commit.index_promotion_{handles.LastFailure}_{handles.LastNativeError}");
        }

        // The live handles (including HEAD's write/delete denial) stay held through the final confirmation and observation.
        try
        {
            var pinnedIndex = handles.TryReadPromotedIndex(out var bytes) ? bytes : null;
            var afterBound = await HeadBoundToBranchAsync(
                gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
            var afterTip = await ReadBranchTipAsync(
                gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
            if (afterBound != true || !afterTip.Ok || !string.Equals(afterTip.Value, facts.CommitSha, StringComparison.Ordinal)
                || !handles.IsHeadBoundTo(facts.BranchName))
            {
                return Ambiguous("local_commit.terminal_confirmation_unproven");
            }

            var observation = await ObserveControlledAsync(
                gitPath, facts.WorkspacePath, commonDirectory, facts.CommitSha,
                Path.Combine(administrativeDirectory, "index"), null, cancellationToken, pinnedIndex);
            return observation.Proven
                ? new LocalCommitExecutionResult(LocalCommitExecutionOutcome.Promoted, "local_commit.promoted", observation.Clean)
                : Ambiguous("local_commit.terminal_observation_" + observation.Reason);
        }
        finally
        {
            heldIndexEffects.TryRemove(facts.OperationId, out _);
            handles.Dispose();
        }
    }

    public Task<bool> ReleaseHeldIndexEffectsAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        if (!TryGetHeld(facts.OperationId, acquisition, out var handles))
        {
            return Task.FromResult(false);
        }

        var removed = handles!.TryDeleteHeldLock();
        if (removed)
        {
            heldIndexEffects.TryRemove(facts.OperationId, out _);
            handles.Dispose();
        }

        return Task.FromResult(removed);
    }

    private bool TryGetHeld(Guid operationId, LocalCommitIndexAcquisition acquisition, out WindowsIndexEffectHandles? handles)
    {
        if (!heldIndexEffects.TryGetValue(operationId, out handles))
        {
            return false;
        }

        return handles.IsReceipt(new WindowsIndexEffectHandles.Receipt(
            acquisition.AdministrativeDirectoryIdentity,
            acquisition.PreimageIdentity,
            acquisition.PreimageLength,
            acquisition.PreparedArtifactIdentity,
            acquisition.PreparedArtifactLength,
            acquisition.LockIdentity,
            acquisition.LockLength,
            acquisition.LockName));
    }

    private async Task<bool> IsExactPreMutationStateAsync(
        string gitPath, LocalCommitFacts facts, string administrativeDirectory, CancellationToken cancellationToken)
    {
        var bound = await HeadBoundToBranchAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(
            gitPath, facts.WorkspacePath, facts.BranchName, cancellationToken, administrativeDirectory);
        return bound == true && tip.Ok && string.Equals(tip.Value, facts.ParentCommitSha, StringComparison.Ordinal)
            && await CommitObjectStateAsync(gitPath, facts.WorkspacePath, facts, cancellationToken, administrativeDirectory)
                == LocalCommitObjectState.ExactMatch;
    }

    private static bool SameDirectory(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>A short, stable reason fragment from a phase-labelled fault such as <c>prepare:ack_timeout</c>.</summary>
    private static string ReasonWord(string reason) =>
        new(reason.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray());
}
