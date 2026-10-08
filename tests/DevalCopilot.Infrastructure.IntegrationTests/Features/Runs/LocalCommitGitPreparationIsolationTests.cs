using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0029 preparation ownership against real Git. Preparation runs before database admission, so concurrent requests (identical
/// operation identifiers included) legitimately prepare at the same time. Two <c>LocalCommitGit</c> instances share one storage
/// root, and a gate at an exact Git command parks one preparation mid-flight while the other runs to its end, so every interleaving
/// is deterministic. Each preparation must own its scratch and prepared-artifact leaves, and no cleanup may reach another's.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitGitPreparationIsolationTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private readonly LocalCommitScene _scene = new();

    public void Dispose() => _scene.Dispose();

    public enum Pause
    {
        /// <summary>The first command of the isolated tree build: the preparation's scratch directory exists, nothing else does.</summary>
        Tree,

        /// <summary>The first command of the controlled observation, inside the observation's own scratch leaf.</summary>
        Observation,

        /// <summary>The first command on the promotion index: the artifact directory exists and the artifact is about to be written.</summary>
        Promotion,
    }

    private static bool Matches(Pause pause, string indexPath) => pause switch
    {
        Pause.Tree => Path.GetFileName(indexPath) == "index" && !indexPath.Contains("controlled-observation", StringComparison.Ordinal),
        Pause.Observation => indexPath.Contains("controlled-observation", StringComparison.Ordinal),
        _ => Path.GetFileName(indexPath) == "promotion.index",
    };

    private async Task<(string Fingerprint, IReadOnlyList<LocalCommitChangedPath> Changes)> ChangedAsync()
    {
        _scene.WriteWorkspace("a.txt", "approved change\n");
        _scene.WriteWorkspace("added/new.txt", "brand new\n");
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        return await _scene.CaptureAsync();
    }

    private string[] Leaves(string folder)
    {
        var path = Path.Combine(_scene.Storage.Root, folder);
        return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private void AssertArtifactIntact(LocalCommitPreparedFacts facts)
    {
        var path = _scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath);
        Assert.NotNull(path);
        Assert.True(File.Exists(path), "the recorded artifact exists");
        Assert.Equal(facts.PreparedIndexSha256, Sha(path), ignoreCase: true);
    }

    [Theory]
    [InlineData(Pause.Tree)]
    [InlineData(Pause.Observation)]
    [InlineData(Pause.Promotion)]
    public async Task Two_preparations_of_one_operation_id_own_distinct_scratch_and_artifact_leaves_whichever_finishes_first(Pause pause)
    {
        var (fingerprint, changes) = await ChangedAsync();
        var operationId = Guid.NewGuid();
        var mainBefore = _scene.MainRepositoryFingerprint();
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(pause, path));
        var slowTask = _scene.PrepareAsync(operationId, fingerprint, changes, _scene.CreateGit(gate));
        var parkedAt = await gate.Reached.WaitAsync(Bound);
        var parkedScratch = Path.GetDirectoryName(parkedAt)!;
        Assert.True(Directory.Exists(parkedScratch));

        var fast = await _scene.PrepareAsync(operationId, fingerprint, changes);

        // The other preparation has run to its end and cleaned up: the parked one still owns everything it had.
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, fast.Result.Outcome);
        Assert.True(Directory.Exists(parkedScratch), "the parked preparation's scratch survives the other one's cleanup");
        // Only a parked Promotion preparation has created its artifact leaf yet; the finished one always has.
        Assert.Equal(pause == Pause.Promotion ? 2 : 1, Leaves("operations").Length);

        gate.Release();
        var slow = await slowTask;

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, slow.Result.Outcome);
        var fastFacts = fast.Result.Facts!;
        var slowFacts = slow.Result.Facts!;
        Assert.NotEqual(fastFacts.PreparedIndexRelativePath, slowFacts.PreparedIndexRelativePath);
        Assert.NotEqual(
            Path.GetDirectoryName(_scene.Storage.ResolveArtifact(fastFacts.PreparedIndexRelativePath)),
            Path.GetDirectoryName(_scene.Storage.ResolveArtifact(slowFacts.PreparedIndexRelativePath)));
        Assert.Equal(fastFacts.TreeSha, slowFacts.TreeSha);
        Assert.Equal(fastFacts.CommitSha, slowFacts.CommitSha);
        Assert.Equal(fastFacts.IndexPreimageSha256, slowFacts.IndexPreimageSha256);
        AssertArtifactIntact(fastFacts);
        AssertArtifactIntact(slowFacts);
        Assert.Empty(Leaves("work"));
        Assert.Equal(2, Leaves("operations").Length);

        // The admitted one (the first to finish) still passes the host's own proofs after the other finished, and executes.
        var admitted = _scene.FactsFor(fast.Request, fastFacts);
        var inspection = await _scene.Git.InspectAsync(admitted, CancellationToken.None);
        Assert.True(inspection.PreparedArtifactIntact);
        var executed = await _scene.Git.ExecuteAsync(admitted, CancellationToken.None);
        Assert.True(executed.Outcome == LocalCommitExecutionOutcome.Promoted, executed.ReasonCode);
        Assert.Equal(admitted.CommitSha, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(mainBefore, _scene.MainRepositoryFingerprint());

        // Terminal cleanup addresses the recorded artifact only; the other preparation's artifact stays (inert, unadmitted).
        Assert.True(await _scene.Git.CleanupAsync(admitted, removeOwnedLock: false, CancellationToken.None));
        Assert.Single(Leaves("operations"));
        Assert.False(File.Exists(_scene.Storage.ResolveArtifact(fastFacts.PreparedIndexRelativePath)));
        Assert.True(File.Exists(_scene.Storage.ResolveArtifact(slowFacts.PreparedIndexRelativePath)));
    }

    [Fact]
    public async Task Terminal_cleanup_of_the_recorded_artifact_never_removes_a_preparation_still_in_flight()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var operationId = Guid.NewGuid();
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(Pause.Promotion, path));
        var inFlight = _scene.PrepareAsync(operationId, fingerprint, changes, _scene.CreateGit(gate));
        var parkedAt = await gate.Reached.WaitAsync(Bound);

        var admitted = await _scene.PrepareAsync(operationId, fingerprint, changes);
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, admitted.Result.Outcome);
        var admittedFacts = _scene.FactsFor(admitted.Request, admitted.Result.Facts!);
        Assert.True(await _scene.Git.CleanupAsync(admittedFacts, removeOwnedLock: false, CancellationToken.None));

        Assert.False(File.Exists(_scene.Storage.ResolveArtifact(admittedFacts.PreparedIndexRelativePath)));
        Assert.True(Directory.Exists(Path.GetDirectoryName(parkedAt)));
        Assert.Single(Leaves("operations"));
        gate.Release();
        var survivor = await inFlight;

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, survivor.Result.Outcome);
        AssertArtifactIntact(survivor.Result.Facts!);
        Assert.Single(Leaves("operations"));
        Assert.Empty(Leaves("work"));
    }

    [Fact]
    public async Task A_refused_preparation_removes_only_its_own_leaves_while_another_is_in_flight()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var operationId = Guid.NewGuid();
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(Pause.Promotion, path));
        var inFlight = _scene.PrepareAsync(operationId, fingerprint, changes, _scene.CreateGit(gate));
        var parkedAt = await gate.Reached.WaitAsync(Bound);

        // Same operation identifier, a checkpoint that is no longer current: refused after its own scratch was built.
        var refused = await _scene.PrepareAsync(operationId, new string('0', 64), changes);

        Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, refused.Result.Outcome);
        Assert.True(Directory.Exists(Path.GetDirectoryName(parkedAt)), "the in-flight preparation's scratch survives the refusal's cleanup");
        Assert.Single(Leaves("operations"));
        gate.Release();
        var survivor = await inFlight;

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, survivor.Result.Outcome);
        AssertArtifactIntact(survivor.Result.Facts!);
        Assert.Single(Leaves("operations"));
        Assert.Empty(Leaves("work"));
    }

    [Fact]
    public async Task A_cancelled_preparation_removes_only_its_own_leaves_while_another_is_in_flight()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var operationId = Guid.NewGuid();
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(Pause.Promotion, path));
        var inFlight = _scene.PrepareAsync(operationId, fingerprint, changes, _scene.CreateGit(gate));
        var parkedAt = await gate.Reached.WaitAsync(Bound);

        using var cancellation = new CancellationTokenSource();
        var cancelling = new CancellingAdapter(new ChildProcessExecutionAdapter(), cancellation, path => Matches(Pause.Observation, path));
        var cancelled = await Record.ExceptionAsync(async () =>
        {
            var attempt = await _scene.PrepareAsync(operationId, fingerprint, changes, _scene.CreateGit(cancelling), cancellation.Token);
            Assert.NotEqual(LocalCommitPreparationOutcome.Prepared, attempt.Result.Outcome);
        });

        Assert.True(cancelling.Cancelled, "the cancellation was requested inside the controlled observation");
        Assert.True(cancelled is null or OperationCanceledException, cancelled?.ToString());
        Assert.True(Directory.Exists(Path.GetDirectoryName(parkedAt)), "the in-flight preparation's scratch survives the cancelled one's cleanup");
        Assert.Single(Leaves("operations"));
        gate.Release();
        var survivor = await inFlight;

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, survivor.Result.Outcome);
        AssertArtifactIntact(survivor.Result.Facts!);
        Assert.Single(Leaves("operations"));
        Assert.Empty(Leaves("work"));
    }

    [Fact]
    public async Task Competing_requests_with_different_operation_ids_keep_independent_leaves()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(Pause.Observation, path));
        var slowTask = _scene.PrepareAsync(Guid.NewGuid(), fingerprint, changes, _scene.CreateGit(gate));
        var parkedAt = await gate.Reached.WaitAsync(Bound);

        var fast = await _scene.PrepareAsync(Guid.NewGuid(), fingerprint, changes);
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, fast.Result.Outcome);
        Assert.True(Directory.Exists(Path.GetDirectoryName(parkedAt)));
        gate.Release();
        var slow = await slowTask;

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, slow.Result.Outcome);
        Assert.NotEqual(fast.Result.Facts!.PreparedIndexRelativePath, slow.Result.Facts!.PreparedIndexRelativePath);
        AssertArtifactIntact(fast.Result.Facts);
        AssertArtifactIntact(slow.Result.Facts);
        Assert.Empty(Leaves("work"));
    }

    [Fact]
    public async Task Overlapping_observations_of_one_operation_never_remove_each_others_scratch()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var prepared = await _scene.PrepareAsync(Guid.NewGuid(), fingerprint, changes);
        var facts = _scene.FactsFor(prepared.Request, prepared.Result.Facts!);
        var executed = await _scene.Git.ExecuteAsync(facts, CancellationToken.None);
        Assert.True(executed.Outcome == LocalCommitExecutionOutcome.Promoted, executed.ReasonCode);

        // After the commit the working files equal the committed tree, so a proven observation reports a consistent source.
        var gate = new GatingAdapter(new ChildProcessExecutionAdapter(), path => Matches(Pause.Observation, path));
        var parked = _scene.CreateGit(gate).InspectAsync(facts, CancellationToken.None);
        var parkedAt = await gate.Reached.WaitAsync(Bound);

        var other = await _scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.True(other.SourceConsistent);
        Assert.True(Directory.Exists(Path.GetDirectoryName(parkedAt)), "the parked observation's scratch survives the other observation's cleanup");
        gate.Release();
        var resumed = await parked;

        Assert.True(resumed.SourceConsistent, "the parked observation completed against its own, intact scratch");
        Assert.Empty(Leaves("work"));
    }

    [Fact]
    public async Task A_legacy_recorded_artifact_path_stays_readable_executable_and_cleanable()
    {
        var (fingerprint, changes) = await ChangedAsync();
        var prepared = await _scene.PrepareAsync(Guid.NewGuid(), fingerprint, changes);
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, prepared.Result.Outcome);
        var mainBefore = _scene.MainRepositoryFingerprint();

        // An operation recorded before this correction names operations\<operation>\prepared.index. Reproduce exactly that layout
        // from the genuine artifact and record that path in the facts.
        var current = _scene.Storage.ResolveArtifact(prepared.Result.Facts!.PreparedIndexRelativePath)!;
        var legacyDirectory = Path.Combine(_scene.Storage.Root, "operations", prepared.Request.OperationId.ToString("N"));
        Directory.CreateDirectory(legacyDirectory);
        File.Copy(current, Path.Combine(legacyDirectory, "prepared.index"));
        var legacyRelative = Path.Combine("operations", prepared.Request.OperationId.ToString("N"), "prepared.index");
        var legacy = _scene.FactsFor(prepared.Request, prepared.Result.Facts with { PreparedIndexRelativePath = legacyRelative });

        var inspection = await _scene.Git.InspectAsync(legacy, CancellationToken.None);
        Assert.True(inspection.PreparedArtifactIntact, "a legacy path is readable");
        var executed = await _scene.Git.ExecuteAsync(legacy, CancellationToken.None);
        Assert.True(executed.Outcome == LocalCommitExecutionOutcome.Promoted, executed.ReasonCode);
        Assert.Equal(legacy.CommitSha, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(mainBefore, _scene.MainRepositoryFingerprint());

        Assert.True(await _scene.Git.CleanupAsync(legacy, removeOwnedLock: false, CancellationToken.None));
        Assert.False(Directory.Exists(legacyDirectory), "the legacy operation directory was cleaned");
        Assert.True(File.Exists(current), "the unrelated current-layout artifact was not touched by the legacy cleanup");
    }

    private sealed class GatingAdapter(IProcessExecutionAdapter inner, Func<string, bool> pauseAt) : IProcessExecutionAdapter
    {
        private readonly TaskCompletionSource<string> _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            if (request.EnvironmentVariables.TryGetValue("GIT_INDEX_FILE", out var index) && pauseAt(index) && _reached.TrySetResult(index))
            {
                await _release.Task.WaitAsync(Bound, cancellationToken);
            }

            return await inner.ExecuteAsync(request, cancellationToken);
        }
    }

    private sealed class CancellingAdapter(IProcessExecutionAdapter inner, CancellationTokenSource cancellation, Func<string, bool> cancelAt)
        : IProcessExecutionAdapter
    {
        public bool Cancelled { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            if (!Cancelled && request.EnvironmentVariables.TryGetValue("GIT_INDEX_FILE", out var index) && cancelAt(index))
            {
                Cancelled = true;
                cancellation.Cancel();
            }

            return inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
