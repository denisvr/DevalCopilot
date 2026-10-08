using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>ADR-0029 execution and recovery proofs against real Git: expected-parent compare-and-swap, owned index promotion,
/// hook suppression, isolation of the main checkout, and every restart shape the recorded facts can prove.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitGitExecutionTests : IDisposable
{
    private readonly LocalCommitScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private async Task<(LocalCommitFacts Facts, LocalCommitPreparationRequest Request)> PreparedAsync()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        _scene.WriteWorkspace("added/new.txt", "brand new\n");
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        var (result, request) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        return (_scene.FactsFor(request, result.Facts!), request);
    }

    [Fact]
    public async Task Execution_moves_only_the_owned_branch_and_leaves_a_clean_index_and_the_main_checkout_untouched()
    {
        var (facts, request) = await PreparedAsync();
        var mainBefore = _scene.MainRepositoryFingerprint();
        var workingFileBefore = File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt"));

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.True(result.SourceConsistent);
        // Inspected first: an ordinary `git status` by anyone may refresh and rewrite the index afterwards.
        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);
        Assert.Equal(LocalCommitIndexState.Prepared, inspection.Index);
        Assert.True(inspection.SourceConsistent);
        Assert.Equal(facts.CommitSha, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(facts.CommitSha, _scene.RunWorkspaceGit("rev-parse", "HEAD").Trim());
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "HEAD^").Trim());
        Assert.Equal(string.Empty, _scene.RunWorkspaceGit("diff-index", "--cached", "HEAD").Trim());
        Assert.Equal(string.Empty, _scene.RunWorkspaceGit("status", "--porcelain").Trim());
        Assert.Equal(workingFileBefore, File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt")));
        Assert.Equal(mainBefore, _scene.MainRepositoryFingerprint());
        Assert.False(File.Exists(_scene.IndexPath + ".lock"));
        Assert.Contains(
            request.OperationId.ToString("D"),
            _scene.RunMainGit("log", "-1", "--format=%B", facts.CommitSha),
            StringComparison.Ordinal);
        Assert.DoesNotContain("gpgsig", _scene.RunMainGit("cat-file", "-p", facts.CommitSha), StringComparison.Ordinal);

        Assert.Equal(LocalCommitObjectState.ExactMatch, inspection.CommitObject);
        Assert.Equal(LocalCommitLockState.None, inspection.IndexLock);
        Assert.Equal(facts.CommitSha, inspection.BranchTipSha);
        Assert.Equal(LocalCommitReferenceLockState.Clear, inspection.ReferenceLocks);
        Assert.True(inspection.OwnershipProven);
        Assert.True(inspection.HeadBoundToBranch);
    }

    [Fact]
    public async Task A_later_external_edit_never_enters_the_prepared_commit_and_is_reported_inconsistent()
    {
        var (facts, _) = await PreparedAsync();
        File.WriteAllText(Path.Combine(_scene.WorkspacePath, "a.txt"), "edited after approval\n");

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.False(result.SourceConsistent);
        Assert.Equal("approved change\n", _scene.RunMainGit("cat-file", "-p", facts.CommitSha + ":a.txt").Replace("\r", string.Empty));
        Assert.Equal("edited after approval\n", File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt")));
    }

    [Fact]
    public async Task Reference_transaction_and_other_hooks_never_run_even_when_configured_everywhere()
    {
        var sentinel = Path.Combine(_scene.Root, "hook-ran.txt");
        var hookBody = $"#!/bin/sh\necho ran >> \"{sentinel.Replace('\\', '/')}\"\n";
        var gitHooks = Path.Combine(_scene.CommonDirectory, "hooks");
        Directory.CreateDirectory(gitHooks);
        foreach (var name in new[] { "reference-transaction", "post-index-change", "post-commit", "pre-commit", "commit-msg" })
        {
            File.WriteAllText(Path.Combine(gitHooks, name), hookBody);
        }

        var configuredHooks = Path.Combine(_scene.Root, "configured-hooks");
        Directory.CreateDirectory(configuredHooks);
        File.WriteAllText(Path.Combine(configuredHooks, "reference-transaction"), hookBody);
        _scene.RunMainGit("config", "core.hooksPath", configuredHooks.Replace('\\', '/'));
        var (facts, _) = await PreparedAsync();

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.Promoted, result.Outcome);
        Assert.False(File.Exists(sentinel), "no hook may run for any Git command of the operation");
    }

    [Fact]
    public async Task A_stale_parent_is_refused_by_compare_and_swap_and_nothing_moves()
    {
        var (facts, _) = await PreparedAsync();
        File.WriteAllText(Path.Combine(_scene.Root, "scratch.txt"), "x");
        var externalTree = _scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
        var external = _scene.RunWorkspaceGit("commit-tree", externalTree, "-p", _scene.BaselineCommit, "-m", "external").Trim();
        _scene.RunWorkspaceGit("update-ref", "refs/heads/" + _scene.BranchName, external, _scene.BaselineCommit);
        var indexBefore = File.ReadAllBytes(_scene.IndexPath);

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal(external, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(indexBefore, File.ReadAllBytes(_scene.IndexPath));
        Assert.False(File.Exists(_scene.IndexPath + ".lock"));
    }

    /// <summary>EARLY-REFUSAL CONTROL, not a race test. The redirect exists before <c>ExecuteAsync</c>, so HEAD already resolves
    /// to the main branch and the initial pre-mutation check refuses before any reference transaction exists. It proves nothing
    /// about the prepared-lock direct-ref proof; the race tests in <c>LocalCommitRefTransactionRealGitTests</c> do.</summary>
    [Fact]
    public async Task Early_refusal_control_a_redirect_installed_before_execution_is_refused_before_any_transaction_starts()
    {
        var (facts, _) = await PreparedAsync();
        var mainBinding = _scene.RunMainGit("symbolic-ref", "HEAD").Trim();
        var mainBefore = _scene.RunMainGit("rev-parse", "HEAD").Trim();
        var mainFingerprint = _scene.MainRepositoryFingerprint();
        var phases = new List<string>();
        var git = _scene.CreateGit(new ChildProcessExecutionAdapter());
        git.RefTransactionObserver = phases.Add;
        _scene.RunMainGit("symbolic-ref", "refs/heads/" + _scene.BranchName, mainBinding);

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal("local_commit.index_acquisition_unproven", result.ReasonCode);
        Assert.Empty(phases);
        Assert.Equal(mainBefore, _scene.RunMainGit("rev-parse", "HEAD").Trim());
        Assert.Equal(mainBinding, _scene.RunMainGit("symbolic-ref", "HEAD").Trim());
        Assert.Equal(mainBinding, _scene.RunMainGit("symbolic-ref", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(mainBinding, _scene.RunWorkspaceGit("symbolic-ref", "HEAD").Trim());
        Assert.Equal(mainFingerprint, _scene.MainRepositoryFingerprint());
    }

    [Fact]
    public async Task A_working_attributes_filter_added_after_preparation_never_runs_during_execution_or_inspection()
    {
        var (facts, _) = await PreparedAsync();
        var sentinel = Path.Combine(_scene.Root, "filter-ran.txt").Replace('\\', '/');
        _scene.RunMainGit("config", "filter.review.clean", $"printf ran > '{sentinel}'; cat");
        _scene.WriteWorkspace(".gitattributes", "* filter=review\n");

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);
        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.False(File.Exists(sentinel));
        Assert.False(inspection.SourceConsistent);
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public async Task Real_common_info_attributes_and_filter_configuration_injected_at_the_observation_seam_never_run()
    {
        var (facts, _) = await PreparedAsync();
        var sentinel = Path.Combine(_scene.Root, "common-filter-ran.txt").Replace('\\', '/');
        var injected = false;
        var git = _scene.CreateGit(new MutatingAdapter(new ChildProcessExecutionAdapter(), request =>
        {
            if (injected || !request.Arguments.Contains("status"))
            {
                return;
            }

            injected = true;
            _scene.RunMainGit("config", "filter.observation.clean", $"printf ran > '{sentinel}'; cat");
            File.WriteAllText(Path.Combine(_scene.CommonDirectory, "info", "attributes"), "* filter=observation\n");
        }));

        var result = await git.ExecuteAsync(facts, CancellationToken.None);
        var inspection = await git.InspectAsync(facts, CancellationToken.None);

        Assert.True(injected);
        Assert.Equal(LocalCommitExecutionOutcome.Promoted, result.Outcome);
        Assert.True(inspection.SourceConsistent);
        Assert.False(File.Exists(sentinel), "the controlled observation must not consult mutable common info/attributes or config");
    }

    [Fact]
    public async Task A_changed_real_index_is_refused_before_any_mutation()
    {
        var (facts, _) = await PreparedAsync();
        _scene.RunWorkspaceGit("add", "a.txt");

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
    }

    [Fact]
    public async Task An_unknown_index_lock_is_never_overwritten_or_removed()
    {
        var (facts, _) = await PreparedAsync();
        var foreignLock = _scene.IndexPath + ".lock";
        File.WriteAllText(foreignLock, "another process owns this");

        var result = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal("another process owns this", File.ReadAllText(foreignLock));
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.False(await _scene.Git.CleanupAsync(facts, removeOwnedLock: true, CancellationToken.None));
        Assert.Equal("another process owns this", File.ReadAllText(foreignLock));
    }

    [Fact]
    public async Task A_foreign_lock_with_identical_prepared_bytes_is_never_adopted_or_removed()
    {
        var (facts, _) = await PreparedAsync();
        var lockPath = _scene.IndexPath + ".lock";
        File.Copy(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, lockPath);

        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);
        var cleaned = await _scene.Git.CleanupAsync(facts, removeOwnedLock: true, CancellationToken.None);

        Assert.Equal(LocalCommitLockState.Unknown, inspection.IndexLock);
        Assert.False(cleaned);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task A_tampered_prepared_artifact_or_missing_commit_object_refuses_before_mutation()
    {
        var (facts, _) = await PreparedAsync();
        File.AppendAllText(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, "tamper");

        var tampered = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);
        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, tampered.Outcome);

        var missing = facts with { CommitSha = new string('1', 40) };
        var absent = await _scene.Git.ExecuteAsync(missing, CancellationToken.None);
        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, absent.Outcome);
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
    }

    [Fact]
    public async Task A_foreign_ownership_marker_refuses_execution()
    {
        var (facts, _) = await PreparedAsync();
        var foreign = facts with { Ownership = facts.Ownership with { LeaseId = Guid.NewGuid() } };

        var result = await _scene.Git.ExecuteAsync(foreign, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
    }

    [Fact]
    public async Task Recovery_proves_an_unstarted_operation_unpromoted_and_removes_only_the_owned_artifact()
    {
        var (facts, _) = await PreparedAsync();

        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.Equal(_scene.BaselineCommit, inspection.BranchTipSha);
        Assert.Equal(LocalCommitIndexState.Preimage, inspection.Index);
        Assert.Equal(LocalCommitLockState.None, inspection.IndexLock);
        Assert.Equal(LocalCommitObjectState.ExactMatch, inspection.CommitObject);
        Assert.True(inspection.PreparedArtifactIntact);
        Assert.True(await _scene.Git.CleanupAsync(facts, removeOwnedLock: false, CancellationToken.None));
        Assert.False(Directory.Exists(_scene.Storage.OperationDirectory(facts.OperationId)));
    }

    [Fact]
    public async Task Recovery_never_adopts_a_lock_merely_because_its_bytes_match_the_prepared_index()
    {
        var (facts, _) = await PreparedAsync();
        File.Copy(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, _scene.IndexPath + ".lock");

        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitLockState.Unknown, inspection.IndexLock);
        Assert.Equal(_scene.BaselineCommit, inspection.BranchTipSha);
        Assert.False(await _scene.Git.CleanupAsync(facts, removeOwnedLock: true, CancellationToken.None));
        Assert.True(File.Exists(_scene.IndexPath + ".lock"));
    }

    [Fact]
    public async Task Recovery_never_promotes_a_pending_lock_that_it_did_not_physically_acquire()
    {
        var (facts, _) = await PreparedAsync();
        _scene.RunWorkspaceGit("update-ref", "refs/heads/" + _scene.BranchName, facts.CommitSha, facts.ParentCommitSha);
        File.Copy(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, _scene.IndexPath + ".lock");

        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);
        Assert.Equal(facts.CommitSha, inspection.BranchTipSha);
        Assert.Equal(LocalCommitIndexState.Preimage, inspection.Index);
        Assert.Equal(LocalCommitLockState.Unknown, inspection.IndexLock);

        var finished = await _scene.Git.FinishIndexPromotionAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.Ambiguous, finished.Outcome);
        Assert.True(File.Exists(_scene.IndexPath + ".lock"));
    }

    [Fact]
    public async Task Recovery_never_finishes_a_promotion_over_an_unknown_lock_or_a_changed_index()
    {
        var (facts, _) = await PreparedAsync();
        _scene.RunWorkspaceGit("update-ref", "refs/heads/" + _scene.BranchName, facts.CommitSha, facts.ParentCommitSha);
        File.WriteAllText(_scene.IndexPath + ".lock", "foreign");

        var unknown = await _scene.Git.FinishIndexPromotionAsync(facts, CancellationToken.None);
        Assert.Equal(LocalCommitExecutionOutcome.Ambiguous, unknown.Outcome);
        Assert.Equal("foreign", File.ReadAllText(_scene.IndexPath + ".lock"));

        File.Delete(_scene.IndexPath + ".lock");
        File.AppendAllText(_scene.IndexPath, "\0");
        var changed = await _scene.Git.FinishIndexPromotionAsync(facts, CancellationToken.None);
        Assert.Equal(LocalCommitExecutionOutcome.Ambiguous, changed.Outcome);
    }

    [Fact]
    public async Task Inspection_reports_an_external_branch_binding_change_and_an_unrecognized_tip()
    {
        var (facts, _) = await PreparedAsync();
        var externalTree = _scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
        var external = _scene.RunWorkspaceGit("commit-tree", externalTree, "-p", _scene.BaselineCommit, "-m", "external").Trim();
        _scene.RunWorkspaceGit("update-ref", "refs/heads/" + _scene.BranchName, external, _scene.BaselineCommit);

        var inspection = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.Equal(external, inspection.BranchTipSha);
        Assert.NotEqual(facts.CommitSha, inspection.BranchTipSha);
        Assert.NotEqual(facts.ParentCommitSha, inspection.BranchTipSha);
    }

    private sealed class MutatingAdapter(IProcessExecutionAdapter inner, Action<ProcessExecutionRequest> before)
        : IProcessExecutionAdapter
    {
        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            before(request);
            return inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
