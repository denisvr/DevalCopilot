using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The scripted repository's loss signalling, proven without a host or Git: a loss point is signalled at the selected point itself,
/// an after-effect point only once the real effect has returned (and only with a successful result), never for method entry, and
/// nothing is left in flight when it is signalled. These cases are what make the restart boundaries deterministic instead of
/// "the call began and some time has passed".
/// </summary>
public sealed class ScriptedLocalCommitRepositoryTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly LocalCommitFacts Facts = new(
        Guid.NewGuid(), "main", "workspace", "branch", new string('a', 40), new string('b', 40), new string('c', 40), "message",
        "name", "email", 1, new string('d', 64), new string('e', 64), @"operations\x\prepared.index",
        new LocalCommitOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1UL, "00"));

    private static readonly LocalCommitIndexAcquisition Acquisition = new("a", "b", 1, "c", 1, "d", 1, "index.lock");

    private static LocalCommitExecutionResult Promoted => new(LocalCommitExecutionOutcome.Promoted, "local_commit.promoted", true);

    [Theory]
    [InlineData(LossPoint.AfterPromoteRef)]
    [InlineData(LossPoint.AfterPromoteHeldIndex)]
    public async Task Method_entry_alone_never_satisfies_an_after_effect_boundary_while_the_real_effect_is_outstanding(LossPoint point)
    {
        var effect = new FakeRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        script.ArmLoss(point);

        var call = Run(script, point);
        await effect.Entered.WaitAsync(Bound);

        // The real effect has begun and has not returned: the call is recorded, one effect is in flight, and nothing is signalled.
        Assert.Contains(point == LossPoint.AfterPromoteRef ? nameof(ILocalCommitRepository.PromoteRefAsync) : nameof(ILocalCommitRepository.PromoteHeldIndexAsync), script.Calls);
        Assert.Equal(1, script.OutstandingEffects);
        Assert.False(script.LossReached.IsCompleted);
        Assert.False(call.IsCompleted);

        effect.Finish(Promoted);
        await Assert.ThrowsAsync<OperationCanceledException>(() => call);

        var report = await script.LossReached.WaitAsync(Bound);
        Assert.Equal(point, report.Point);
        Assert.True(report.Satisfied);
        Assert.Equal(0, report.OutstandingEffects);
        Assert.Equal(0, script.OutstandingEffects);
        Assert.Contains("Promoted", report.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LossPoint.AfterPromoteRef, LocalCommitExecutionOutcome.NotPromoted, "local_commit.reference_refused")]
    [InlineData(LossPoint.AfterPromoteRef, LocalCommitExecutionOutcome.Ambiguous, "local_commit.execution_error")]
    [InlineData(LossPoint.AfterPromoteHeldIndex, LocalCommitExecutionOutcome.NotPromoted, "local_commit.index_refused")]
    [InlineData(LossPoint.AfterPromoteHeldIndex, LocalCommitExecutionOutcome.Ambiguous, "local_commit.index_unproven")]
    public async Task A_refusal_or_failure_of_the_real_effect_is_reported_with_its_reason_and_is_not_a_reached_boundary(
        LossPoint point, LocalCommitExecutionOutcome outcome, string reason)
    {
        var effect = new FakeRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        script.ArmLoss(point);

        var call = Run(script, point);
        await effect.Entered.WaitAsync(Bound);
        effect.Finish(new LocalCommitExecutionResult(outcome, reason, false));
        await Assert.ThrowsAsync<OperationCanceledException>(() => call);

        var report = await script.LossReached.WaitAsync(Bound);
        Assert.False(report.Satisfied);
        Assert.Contains(outcome.ToString(), report.Detail, StringComparison.Ordinal);
        Assert.Contains(reason, report.Detail, StringComparison.Ordinal);
        Assert.InRange(report.Detail.Length, 1, 200);
        Assert.Equal(0, report.OutstandingEffects);
    }

    [Theory]
    [InlineData(LossPoint.BeforeAcquire)]
    [InlineData(LossPoint.BeforePromoteRef)]
    [InlineData(LossPoint.BeforePromoteHeldIndex)]
    public async Task A_before_effect_boundary_is_signalled_at_the_hook_and_the_real_effect_is_never_started(LossPoint point)
    {
        var effect = new FakeRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        script.ArmLoss(point);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(script, point));

        var report = await script.LossReached.WaitAsync(Bound);
        Assert.Equal(point, report.Point);
        Assert.True(report.Satisfied);
        Assert.Equal(0, report.OutstandingEffects);
        Assert.Equal(0, effect.EffectsStarted);
    }

    [Fact]
    public async Task A_call_made_after_the_loss_is_listed_so_a_lost_host_that_keeps_acting_is_visible()
    {
        var effect = new FakeRepository();
        var script = new ScriptedLocalCommitRepository(effect);
        script.ArmLoss(LossPoint.BeforePromoteRef);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(script, LossPoint.BeforePromoteRef));
        Assert.Empty(script.CallsAfterLoss);

        await script.CleanupAsync(Facts, removeOwnedLock: false, CancellationToken.None);

        Assert.Single(script.CallsAfterLoss);
    }

    private static Task Run(ScriptedLocalCommitRepository script, LossPoint point) => point switch
    {
        LossPoint.BeforeAcquire => script.AcquireIndexEffectsAsync(Facts, CancellationToken.None),
        LossPoint.BeforePromoteRef or LossPoint.AfterPromoteRef => script.PromoteRefAsync(Facts, Acquisition, CancellationToken.None),
        _ => script.PromoteHeldIndexAsync(Facts, Acquisition, "quarantine", CancellationToken.None),
    };

    /// <summary>An inner repository whose real effects block until the test finishes them.</summary>
    private sealed class FakeRepository : ILocalCommitRepository
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<LocalCommitExecutionResult> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _effectsStarted;

        public Task Entered => _entered.Task;

        public int EffectsStarted => Volatile.Read(ref _effectsStarted);

        public void Finish(LocalCommitExecutionResult result) => _finish.TrySetResult(result);

        private Task<LocalCommitExecutionResult> Effect()
        {
            Interlocked.Increment(ref _effectsStarted);
            _entered.TrySetResult();
            return _finish.Task;
        }

        public Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _effectsStarted);
            return Task.FromResult<LocalCommitIndexAcquisition?>(Acquisition);
        }

        public Task<LocalCommitExecutionResult> PromoteRefAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken) => Effect();

        public Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, string quarantineName, CancellationToken cancellationToken) =>
            Effect();

        public Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(
            LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ReleaseHeldIndexEffectsAsync(
            LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
