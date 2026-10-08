using System.Diagnostics;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0029 R1 at the production boundary with real Git. The owner is the production
/// <c>LocalCommitRefTransaction</c> driving a real <c>git update-ref --no-deref --stdin</c>; the observer is a deterministic seam
/// that places an external change at an exact transaction phase. Every race test installs its interference AFTER the initial and
/// acquisition checks passed, so only the prepared-lock proof can defend, and each asserts which phases were reached.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitRefTransactionRealGitTests : IDisposable
{
    private readonly LocalCommitScene _scene = new();
    private readonly List<string> _phases = [];

    public void Dispose() => _scene.Dispose();

    private async Task<LocalCommitFacts> PreparedAsync()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        _scene.WriteWorkspace("added/new.txt", "brand new\n");
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        var (result, request) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        return _scene.FactsFor(request, result.Facts!);
    }

    /// <summary>The operation holds the index with delete access, so an ordinary read must share delete too.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private string BranchRef => "refs/heads/" + _scene.BranchName;

    private string BranchRefLock => Path.Combine(
        _scene.CommonDirectory, "refs", "heads", _scene.BranchName.Replace('/', '\\') + ".lock");

    private string HeadPath => Path.Combine(_scene.AdministrativeDirectory, "HEAD");

    private LocalCommitGit ObservedGit(Action<string> onPhase, IProcessExecutionAdapter? process = null)
    {
        var git = _scene.CreateGit(process ?? new ChildProcessExecutionAdapter());
        git.RefTransactionObserver = phase =>
        {
            _phases.Add(phase);
            onPhase(phase);
        };
        return git;
    }

    private string ExternalCommit()
    {
        var tree = _scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
        return _scene.RunWorkspaceGit("commit-tree", tree, "-p", _scene.BaselineCommit, "-m", "external").Trim();
    }

    private void AssertNoRefOrIndexLocks()
    {
        Assert.False(File.Exists(BranchRefLock), "a prepared branch lock remains");
        Assert.False(File.Exists(_scene.IndexPath + ".lock"), "an index lock remains");
    }

    [Fact]
    public async Task Normal_success_control_reaches_every_phase_in_order_and_leaves_no_lock()
    {
        var facts = await PreparedAsync();
        var git = ObservedGit(_ => { });

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        string[] expected =
        [
            "before_start", "launched", "start_sent", "start_acknowledged", "update_sent", "prepare_sent", "prepared",
            "proof_started", "proof_passed", "commit_sent", "commit_acknowledged", "committed",
        ];
        Assert.Equal(expected, _phases);
        Assert.Equal(facts.CommitSha, _scene.RunWorkspaceGit("rev-parse", BranchRef).Trim());
        AssertNoRefOrIndexLocks();
    }

    [Fact]
    public async Task A_redirect_installed_after_the_initial_checks_is_caught_by_the_prepared_direct_ref_proof()
    {
        var facts = await PreparedAsync();
        var mainBinding = _scene.RunMainGit("symbolic-ref", "HEAD").Trim();
        var mainBefore = _scene.RunMainGit("rev-parse", "HEAD").Trim();
        var mainFingerprint = _scene.MainRepositoryFingerprint();
        var workspaceBefore = File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt"));
        var indexBefore = File.ReadAllBytes(_scene.IndexPath);
        var git = ObservedGit(phase =>
        {
            if (phase == "before_start")
            {
                // Initial and acquisition checks have all passed; the branch becomes a redirect to the main checkout's branch.
                _scene.RunMainGit("symbolic-ref", BranchRef, mainBinding);
            }
        });

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal("local_commit.ref_transaction_aborted", result.ReasonCode);
        Assert.Equal(
            ["before_start", "launched", "start_sent", "start_acknowledged", "update_sent", "prepare_sent", "prepared",
                "proof_started", "proof_fault:ref_not_direct", "proof_failed", "abort_sent", "abort_acknowledged", "aborted"],
            _phases);
        Assert.DoesNotContain("commit_sent", _phases);
        Assert.Equal(mainBinding, _scene.RunMainGit("symbolic-ref", BranchRef).Trim());
        Assert.Equal(mainBefore, _scene.RunMainGit("rev-parse", "HEAD").Trim());
        Assert.Equal(mainBinding, _scene.RunMainGit("symbolic-ref", "HEAD").Trim());
        Assert.Equal(mainFingerprint, _scene.MainRepositoryFingerprint());
        Assert.Equal(workspaceBefore, File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt")));
        Assert.Equal(indexBefore, File.ReadAllBytes(_scene.IndexPath));
        AssertNoRefOrIndexLocks();
    }

    [Fact]
    public async Task A_stale_parent_appearing_between_the_checks_and_prepare_is_refused_by_the_provider_and_retained()
    {
        var facts = await PreparedAsync();
        var external = ExternalCommit();
        var mainFingerprint = _scene.MainRepositoryFingerprint();
        var git = ObservedGit(phase =>
        {
            if (phase == "before_start")
            {
                _scene.RunWorkspaceGit("update-ref", BranchRef, external, _scene.BaselineCommit);
            }
        });

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.DoesNotContain("prepared", _phases);
        Assert.DoesNotContain("commit_sent", _phases);
        Assert.Equal(external, _scene.RunWorkspaceGit("rev-parse", BranchRef).Trim());
        Assert.Equal(mainFingerprint, _scene.MainRepositoryFingerprint());
        AssertNoRefOrIndexLocks();
    }

    [Fact]
    public async Task While_prepared_competing_git_writers_to_the_branch_and_head_are_refused_and_the_commit_still_succeeds()
    {
        var facts = await PreparedAsync();
        var external = ExternalCommit();
        var mainBinding = _scene.RunMainGit("symbolic-ref", "HEAD").Trim();
        var refused = new List<(string Label, int ExitCode)>();
        var git = ObservedGit(phase =>
        {
            if (phase != "prepared")
            {
                return;
            }

            refused.Add(("update-ref", _scene.RunGitRaw(
                _scene.WorkspacePath, "update-ref", BranchRef, external, _scene.BaselineCommit).ExitCode));
            refused.Add(("branch-redirect", _scene.RunGitRaw(
                _scene.WorkspacePath, "symbolic-ref", BranchRef, mainBinding).ExitCode));
            refused.Add(("head-redirect", _scene.RunGitRaw(
                _scene.WorkspacePath, "symbolic-ref", "HEAD", mainBinding).ExitCode));
        });

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.Equal(3, refused.Count);
        Assert.All(refused, item => Assert.True(item.ExitCode != 0, item.Label + " was not refused"));
        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.Equal(facts.CommitSha, _scene.RunWorkspaceGit("rev-parse", BranchRef).Trim());
        Assert.Equal("refs/heads/" + _scene.BranchName, _scene.RunWorkspaceGit("symbolic-ref", "HEAD").Trim());
    }

    [Fact]
    public async Task The_live_head_handle_refuses_git_and_raw_writers_deletion_and_rename_from_acquisition_to_disposal()
    {
        var facts = await PreparedAsync();
        var git = _scene.Git;
        var acquisition = await git.AcquireIndexEffectsAsync(facts, CancellationToken.None);
        Assert.NotNull(acquisition);
        var before = File.ReadAllBytes(HeadPath);
        try
        {
            Assert.True(_scene.RunGitRaw(_scene.WorkspacePath, "symbolic-ref", "HEAD", "refs/heads/other").ExitCode != 0);
            Assert.True(_scene.RunGitRaw(_scene.WorkspacePath, "checkout", "--detach", _scene.BaselineCommit).ExitCode != 0);
            Assert.ThrowsAny<IOException>(() => File.WriteAllText(HeadPath, "ref: refs/heads/other\n"));
            Assert.ThrowsAny<Exception>(() => File.Delete(HeadPath));
            Assert.ThrowsAny<Exception>(() => File.Move(HeadPath, HeadPath + ".moved"));
            Assert.Equal(before, File.ReadAllBytes(HeadPath));
            Assert.True(_scene.RunGitRaw(_scene.WorkspacePath, "rev-parse", "HEAD").ExitCode == 0, "reads must not be blocked");
        }
        finally
        {
            Assert.True(await git.ReleaseHeldIndexEffectsAsync(facts, acquisition, CancellationToken.None));
        }

        // Disposal is explicit: after release the file is writable again.
        File.WriteAllBytes(HeadPath, before);
        Assert.Equal(before, File.ReadAllBytes(HeadPath));
    }

    [Fact]
    public async Task The_head_handle_is_retained_through_the_reference_effect_and_released_after_the_final_observation()
    {
        var facts = await PreparedAsync();
        var attempts = new List<int>();
        var git = ObservedGit(phase =>
        {
            if (phase is "prepared" or "committed")
            {
                attempts.Add(_scene.RunGitRaw(_scene.WorkspacePath, "symbolic-ref", "HEAD", "refs/heads/other").ExitCode);
            }
        });

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, exitCode => Assert.NotEqual(0, exitCode));
        Assert.Equal("refs/heads/" + _scene.BranchName, _scene.RunWorkspaceGit("symbolic-ref", "HEAD").Trim());
        // Released: ordinary Git may now rebind HEAD, which proves the handle did not outlive the operation.
        _scene.RunWorkspaceGit("symbolic-ref", "HEAD", "refs/heads/" + _scene.BranchName);
    }

    [Fact]
    public async Task The_head_handle_is_still_held_while_the_index_effect_is_confirmed_by_the_final_observation()
    {
        var facts = await PreparedAsync();
        var armed = false;
        var attempts = new List<int>();
        var process = new InterceptingAdapter(new ChildProcessExecutionAdapter(), request =>
        {
            if (armed && request.Arguments.Contains("status"))
            {
                armed = false;
                attempts.Add(_scene.RunGitRaw(_scene.WorkspacePath, "symbolic-ref", "HEAD", "refs/heads/elsewhere").ExitCode);
            }

            return Task.CompletedTask;
        });
        var git = ObservedGit(phase => armed |= phase == "committed", process);

        var result = await git.ExecuteAsync(facts, CancellationToken.None);

        // The only status read after the reference moved is the controlled observation that follows the index promotion.
        Assert.NotEqual(0, Assert.Single(attempts));
        Assert.True(result.Outcome == LocalCommitExecutionOutcome.Promoted, result.ReasonCode);
        Assert.Equal("refs/heads/" + _scene.BranchName, _scene.RunWorkspaceGit("symbolic-ref", "HEAD").Trim());
    }

    [Fact]
    public async Task Proof_reads_use_the_proven_git_directory_and_ignore_a_workspace_git_file_redirected_mid_proof()
    {
        var facts = await PreparedAsync();
        var gitFile = Path.Combine(_scene.WorkspacePath, ".git");
        var original = File.ReadAllText(gitFile);
        var originalAttributes = File.GetAttributes(gitFile);
        var armed = false;
        var redirected = false;
        var process = new InterceptingAdapter(new ChildProcessExecutionAdapter(), _ =>
        {
            if (!armed || redirected)
            {
                return Task.CompletedTask;
            }

            redirected = true;
            File.SetAttributes(gitFile, FileAttributes.Normal);
            File.WriteAllText(gitFile, "gitdir: " + Path.Combine(_scene.MainPath, ".git").Replace('\\', '/') + "\n");
            return Task.CompletedTask;
        });
        var git = ObservedGit(phase => armed |= phase == "proof_started", process);
        var acquisition = await git.AcquireIndexEffectsAsync(facts, CancellationToken.None);
        Assert.NotNull(acquisition);
        try
        {
            var promoted = await git.PromoteRefAsync(facts, acquisition, CancellationToken.None);

            // Explicit context: no proof read rediscovered the repository through the redirected workspace file.
            Assert.True(redirected);
            Assert.True(promoted.Outcome == LocalCommitExecutionOutcome.Promoted, promoted.ReasonCode);
            Assert.Equal(facts.CommitSha, _scene.RunMainGit("rev-parse", BranchRef).Trim());

            // The index effect re-proves ownership through the workspace and fails closed instead of guessing.
            var indexBefore = ReadShared(_scene.IndexPath);
            var pending = await git.PromoteHeldIndexAsync(
                facts, acquisition, $"devalcopilot-{facts.OperationId:N}.index-preimage", CancellationToken.None);
            Assert.Equal(LocalCommitExecutionOutcome.Ambiguous, pending.Outcome);
            Assert.Equal(indexBefore, ReadShared(_scene.IndexPath));
        }
        finally
        {
            File.SetAttributes(gitFile, FileAttributes.Normal);
            File.WriteAllText(gitFile, original);
            File.SetAttributes(gitFile, originalAttributes);
            await git.ReleaseHeldIndexEffectsAsync(facts, acquisition, CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_stalled_proof_read_is_bounded_by_the_transaction_deadline_then_aborted_in_its_own_budget()
    {
        var facts = await PreparedAsync();
        var armed = false;
        var stalls = 0;
        var process = new InterceptingAdapter(new ChildProcessExecutionAdapter(), async (request, token) =>
        {
            if (armed && request.Arguments.Contains("symbolic-ref"))
            {
                stalls++;
                await Task.Delay(Timeout.Infinite, token);
            }
        });
        var git = ObservedGit(phase => armed |= phase == "proof_started", process);
        git.RefTransactionBudgets = new LocalCommitRefTransactionBudgets(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));
        var mainFingerprint = _scene.MainRepositoryFingerprint();
        var stopwatch = Stopwatch.StartNew();

        var result = await git.ExecuteAsync(facts, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(40));
        stopwatch.Stop();

        Assert.Equal(1, stalls);
        Assert.Equal(LocalCommitExecutionOutcome.NotPromoted, result.Outcome);
        Assert.Equal("local_commit.ref_transaction_aborted", result.ReasonCode);
        Assert.Contains("proof_failed", _phases);
        Assert.Contains("aborted", _phases);
        Assert.DoesNotContain("commit_sent", _phases);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"unbounded: {stopwatch.Elapsed}");
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", BranchRef).Trim());
        Assert.Equal(mainFingerprint, _scene.MainRepositoryFingerprint());
        AssertNoRefOrIndexLocks();
    }

    [Fact]
    public async Task Caller_cancellation_while_prepared_aborts_under_its_own_budget_and_propagates()
    {
        var facts = await PreparedAsync();
        using var cancellation = new CancellationTokenSource();
        var git = ObservedGit(phase =>
        {
            if (phase == "proof_started")
            {
                cancellation.Cancel();
            }
        });
        var acquisition = await git.AcquireIndexEffectsAsync(facts, CancellationToken.None);
        Assert.NotNull(acquisition);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => git.PromoteRefAsync(facts, acquisition, cancellation.Token));

            Assert.Contains("aborted", _phases);
            Assert.DoesNotContain("commit_sent", _phases);
            Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", BranchRef).Trim());
            Assert.False(File.Exists(BranchRefLock));
        }
        finally
        {
            Assert.True(await git.ReleaseHeldIndexEffectsAsync(facts, acquisition, CancellationToken.None));
        }
    }

    /// <summary>Awaits an async hook before each child process request of the production adapter, so a test can place an external
    /// change (or an unbounded stall) between two real Git calls.</summary>
    private sealed class InterceptingAdapter : IProcessExecutionAdapter
    {
        private readonly IProcessExecutionAdapter _inner;
        private readonly Func<ProcessExecutionRequest, CancellationToken, Task> _before;

        public InterceptingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, Task> before)
            : this(inner, (request, _) => before(request))
        {
        }

        public InterceptingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, CancellationToken, Task> before)
        {
            _inner = inner;
            _before = before;
        }

        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            await _before(request, cancellationToken);
            return await _inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
