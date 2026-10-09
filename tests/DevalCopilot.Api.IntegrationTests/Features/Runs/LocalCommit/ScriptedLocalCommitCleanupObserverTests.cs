using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The scripted repository's cleanup signal, proven without a host or Git by an inner repository whose cleanup blocks until the
/// test finishes it: entering the cleanup signals nothing, the signal fires only once the real cleanup returned or ended, and it
/// keeps the cleanup's own result and the operation and artifact it addressed, so a refused or failed cleanup is never reported as
/// a successful removal.
/// </summary>
public sealed class ScriptedLocalCommitCleanupObserverTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly LocalCommitFacts Facts = new(
        Guid.NewGuid(), "main", "workspace", "branch", new string('a', 40), new string('b', 40), new string('c', 40), "message",
        "name", "email", 1, new string('d', 64), new string('e', 64), @"operations\x\prepared.index",
        new LocalCommitOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1UL, "00"));

    [Fact]
    public async Task Entering_the_cleanup_signals_nothing_until_the_real_cleanup_has_returned()
    {
        var effect = new BlockingCleanupRepository();
        var script = new ScriptedLocalCommitRepository(effect);

        var call = script.CleanupAsync(Facts, removeOwnedLock: false, CancellationToken.None);
        await effect.Entered.WaitAsync(Bound);

        Assert.Contains("CleanupAsync(False)", script.Calls);
        Assert.False(script.CleanupReturned.IsCompleted, "the cleanup was reported before it returned");
        Assert.Empty(script.CleanupReports);
        Assert.False(call.IsCompleted);

        effect.Finish(true);
        Assert.True(await call.WaitAsync(Bound));

        var report = await script.CleanupReturned.WaitAsync(Bound);
        Assert.True(report.Succeeded);
        Assert.True(report.Result);
        Assert.Equal(Facts.OperationId, report.OperationId);
        Assert.Equal(Facts.PreparedIndexRelativePath, report.ArtifactRelativePath);
        Assert.False(report.RemoveOwnedLock);
        Assert.Same(report, Assert.Single(script.CleanupReports));
    }

    [Fact]
    public async Task A_cleanup_that_returned_false_is_reported_as_not_succeeded()
    {
        var effect = new BlockingCleanupRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        var call = script.CleanupAsync(Facts, removeOwnedLock: true, CancellationToken.None);
        await effect.Entered.WaitAsync(Bound);

        effect.Finish(false);

        Assert.False(await call.WaitAsync(Bound));
        var report = await script.CleanupReturned.WaitAsync(Bound);
        Assert.False(report.Succeeded);
        Assert.False(report.Result);
        Assert.True(report.RemoveOwnedLock);
        Assert.Equal(Facts.PreparedIndexRelativePath, report.ArtifactRelativePath);
    }

    [Fact]
    public async Task A_cleanup_that_throws_is_reported_without_a_result_and_the_exception_still_propagates()
    {
        var effect = new BlockingCleanupRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        var call = script.CleanupAsync(Facts, removeOwnedLock: false, CancellationToken.None);
        await effect.Entered.WaitAsync(Bound);

        effect.Fail(new IOException("locked"));

        await Assert.ThrowsAsync<IOException>(() => call);
        var report = await script.CleanupReturned.WaitAsync(Bound);
        Assert.False(report.Succeeded);
        Assert.Null(report.Result);
        Assert.Equal(Facts.OperationId, report.OperationId);
    }

    /// <summary>An inner repository whose cleanup blocks until the test finishes or fails it.</summary>
    private sealed class BlockingCleanupRepository : ILocalCommitRepository
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Finish(bool result) => _finish.SetResult(result);

        public void Fail(Exception exception) => _finish.SetException(exception);

        public Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            return _finish.Task;
        }

        public Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(
            LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> PromoteRefAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, string quarantineName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ReleaseHeldIndexEffectsAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
