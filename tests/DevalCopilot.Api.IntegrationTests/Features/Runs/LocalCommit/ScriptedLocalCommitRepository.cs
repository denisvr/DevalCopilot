using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// A transparent decorator over the REAL Git adapter. Every call reaches real Git; a test attaches a hook to place an external
/// change immediately before an effect, to replace or fault an outcome after the real effect happened, or to simulate host loss.
/// Host loss is armed with <see cref="ArmLoss"/>: the loss is signalled (<see cref="LossReached"/>) at the selected point itself,
/// for an after-effect point only after the real effect has returned and been captured, and then unwinds the execution with
/// <see cref="OperationCanceledException"/> (the handler deliberately leaves the durable marker untouched for that). Entering a
/// method never signals anything. The decorator records the order of calls, counts the real effects in flight, notes any call made
/// after the loss, and keeps the live acquisition so a test (or the fixture's failure-safe cleanup) can finally release the
/// in-process capability it deliberately left held, the way process death would. It also reports every real cleanup
/// (<see cref="CleanupReports"/>, <see cref="CleanupReturned"/>) only after that cleanup returned or ended, with its own result.
/// </summary>
internal sealed class ScriptedLocalCommitRepository(ILocalCommitRepository inner) : ILocalCommitRepository
{
    private const int MaximumDetailCharacters = 200;

    private readonly object _gate = new();
    private readonly TaskCompletionSource<LossReport> _lossReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<LocalCommitCleanupReport> _cleanupReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<LocalCommitCleanupReport> _cleanups = [];
    private LossPoint? _armed;
    private int _outstanding;

    public List<string> Calls { get; } = [];

    /// <summary>Completes with the first real cleanup's report once that cleanup returned or ended; entering it signals nothing.
    /// The report keeps the cleanup's own result and the operation and artifact it addressed, so a failure is never read as success.</summary>
    public Task<LocalCommitCleanupReport> CleanupReturned => _cleanupReturned.Task;

    /// <summary>Every cleanup that has returned or ended so far, in order.</summary>
    public IReadOnlyList<LocalCommitCleanupReport> CleanupReports
    {
        get
        {
            lock (_gate)
            {
                return [.. _cleanups];
            }
        }
    }

    /// <summary>Calls the decorator received after the loss was signalled. A lost host performs nothing further.</summary>
    public List<string> CallsAfterLoss { get; } = [];

    public LocalCommitFacts? LastFacts { get; private set; }

    public LocalCommitIndexAcquisition? LastAcquisition { get; private set; }

    public Func<LocalCommitFacts, Task>? BeforeAcquire { get; set; }

    public Func<LocalCommitFacts, Task>? BeforePromoteRef { get; set; }

    public Func<LocalCommitFacts, LocalCommitExecutionResult, Task<LocalCommitExecutionResult>>? AfterPromoteRef { get; set; }

    public Func<LocalCommitFacts, Task>? BeforePromoteHeldIndex { get; set; }

    public Func<LocalCommitFacts, LocalCommitExecutionResult, Task<LocalCommitExecutionResult>>? AfterPromoteHeldIndex { get; set; }

    public Func<LocalCommitFacts, Task>? BeforeInspect { get; set; }

    public Func<LocalCommitFacts, LocalCommitInspection, LocalCommitInspection>? InspectOverride { get; set; }

    /// <summary>Completes with the report once the armed loss point is reached; it never completes for method entry alone.</summary>
    public Task<LossReport> LossReached => _lossReached.Task;

    /// <summary>The number of real external effects (acquire, reference, held index) that were entered and have not returned.</summary>
    public int OutstandingEffects => Volatile.Read(ref _outstanding);

    public void ArmLoss(LossPoint point) => _armed = point;

    private void Record(string call)
    {
        lock (_gate)
        {
            Calls.Add(call);
            if (_lossReached.Task.IsCompleted)
            {
                CallsAfterLoss.Add(call);
            }
        }
    }

    /// <summary>Signals the armed loss and unwinds. The result of the real effect is captured first, so a refusal or a failure is
    /// reported with its bounded outcome and reason and never mistaken for the effect having happened.</summary>
    private void LoseHost(LossPoint point, LocalCommitExecutionResult? realResult)
    {
        var after = point is LossPoint.AfterPromoteRef or LossPoint.AfterPromoteHeldIndex;
        var satisfied = !after || realResult?.Outcome == LocalCommitExecutionOutcome.Promoted;
        var detail = after
            ? $"{point}: the real effect returned {realResult?.Outcome} ({realResult?.ReasonCode})"
            : $"{point}: reached before the real effect";
        _lossReached.TrySetResult(new LossReport(
            point,
            satisfied,
            detail.Length <= MaximumDetailCharacters ? detail : detail[..MaximumDetailCharacters],
            OutstandingEffects));
        throw new OperationCanceledException("The host was lost.");
    }

    private bool IsArmed(LossPoint point) => _armed == point;

    private async Task<T> EffectAsync<T>(Func<Task<T>> effect)
    {
        Interlocked.Increment(ref _outstanding);
        try
        {
            return await effect();
        }
        finally
        {
            Interlocked.Decrement(ref _outstanding);
        }
    }

    public async Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Record(nameof(AcquireIndexEffectsAsync));
        LastFacts = facts;
        if (BeforeAcquire is not null)
        {
            await BeforeAcquire(facts);
        }

        if (IsArmed(LossPoint.BeforeAcquire))
        {
            LoseHost(LossPoint.BeforeAcquire, null);
        }

        var acquisition = await EffectAsync(() => inner.AcquireIndexEffectsAsync(facts, cancellationToken));
        LastAcquisition = acquisition ?? LastAcquisition;
        return acquisition;
    }

    public Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(
        LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken)
    {
        Record(nameof(AcquirePendingIndexEffectsAsync));
        return inner.AcquirePendingIndexEffectsAsync(facts, quarantineName, cancellationToken);
    }

    public async Task<LocalCommitExecutionResult> PromoteRefAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        Record(nameof(PromoteRefAsync));
        if (BeforePromoteRef is not null)
        {
            await BeforePromoteRef(facts);
        }

        if (IsArmed(LossPoint.BeforePromoteRef))
        {
            LoseHost(LossPoint.BeforePromoteRef, null);
        }

        var result = await EffectAsync(() => inner.PromoteRefAsync(facts, acquisition, cancellationToken));
        if (IsArmed(LossPoint.AfterPromoteRef))
        {
            LoseHost(LossPoint.AfterPromoteRef, result);
        }

        return AfterPromoteRef is null ? result : await AfterPromoteRef(facts, result);
    }

    public async Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, string quarantineName, CancellationToken cancellationToken)
    {
        Record(nameof(PromoteHeldIndexAsync));
        if (BeforePromoteHeldIndex is not null)
        {
            await BeforePromoteHeldIndex(facts);
        }

        if (IsArmed(LossPoint.BeforePromoteHeldIndex))
        {
            LoseHost(LossPoint.BeforePromoteHeldIndex, null);
        }

        var result = await EffectAsync(() => inner.PromoteHeldIndexAsync(facts, acquisition, quarantineName, cancellationToken));
        if (IsArmed(LossPoint.AfterPromoteHeldIndex))
        {
            LoseHost(LossPoint.AfterPromoteHeldIndex, result);
        }

        return AfterPromoteHeldIndex is null ? result : await AfterPromoteHeldIndex(facts, result);
    }

    public Task<bool> ReleaseHeldIndexEffectsAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        Record(nameof(ReleaseHeldIndexEffectsAsync));
        return inner.ReleaseHeldIndexEffectsAsync(facts, acquisition, cancellationToken);
    }

    public Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteAsync));
        return inner.ExecuteAsync(facts, cancellationToken);
    }

    public async Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Record(nameof(InspectAsync));
        if (BeforeInspect is not null)
        {
            await BeforeInspect(facts);
        }

        var inspection = await inner.InspectAsync(facts, cancellationToken);
        return InspectOverride is null ? inspection : InspectOverride(facts, inspection);
    }

    public Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Record(nameof(FinishIndexPromotionAsync));
        return inner.FinishIndexPromotionAsync(facts, cancellationToken);
    }

    public async Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken)
    {
        Record($"{nameof(CleanupAsync)}({removeOwnedLock})");
        bool? result = null;
        try
        {
            result = await inner.CleanupAsync(facts, removeOwnedLock, cancellationToken);
            return result.Value;
        }
        finally
        {
            PublishCleanup(new LocalCommitCleanupReport(facts.OperationId, facts.PreparedIndexRelativePath, removeOwnedLock, result));
        }
    }

    private void PublishCleanup(LocalCommitCleanupReport report)
    {
        lock (_gate)
        {
            _cleanups.Add(report);
        }

        _cleanupReturned.TrySetResult(report);
    }

    /// <summary>Releases the live handles this decorator saw acquired, ending the in-process capability that a simulated host
    /// loss left behind. It goes through the repository's own release API, which deletes the lock only through the handle that
    /// created it and refuses anything it does not own (a foreign lock, an already released or never held capability returns
    /// false and nothing is touched). It never deletes a lock by pathname.</summary>
    public async Task<bool> ReleaseRetainedAsync()
    {
        if (LastFacts is null || LastAcquisition is null)
        {
            return false;
        }

        return await inner.ReleaseHeldIndexEffectsAsync(LastFacts, LastAcquisition, CancellationToken.None);
    }
}
