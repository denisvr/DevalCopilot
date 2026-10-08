using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// A transparent decorator over the REAL Git adapter. Every call reaches real Git; a test attaches a hook to place an external
/// change immediately before an effect, to replace or fault an outcome after the real effect happened, or to simulate host loss by
/// throwing <see cref="OperationCanceledException"/> (the handler deliberately leaves the durable marker untouched for that).
/// It records the order of calls and keeps the live acquisition so a test can finally release the in-process capability it
/// deliberately left held, the way process death would.
/// </summary>
internal sealed class ScriptedLocalCommitRepository(ILocalCommitRepository inner) : ILocalCommitRepository
{
    public List<string> Calls { get; } = [];

    public LocalCommitFacts? LastFacts { get; private set; }

    public LocalCommitIndexAcquisition? LastAcquisition { get; private set; }

    public Func<LocalCommitFacts, Task>? BeforeAcquire { get; set; }

    public Func<LocalCommitFacts, Task>? BeforePromoteRef { get; set; }

    public Func<LocalCommitFacts, LocalCommitExecutionResult, Task<LocalCommitExecutionResult>>? AfterPromoteRef { get; set; }

    public Func<LocalCommitFacts, Task>? BeforePromoteHeldIndex { get; set; }

    public Func<LocalCommitFacts, LocalCommitExecutionResult, Task<LocalCommitExecutionResult>>? AfterPromoteHeldIndex { get; set; }

    public Func<LocalCommitFacts, Task>? BeforeInspect { get; set; }

    public Func<LocalCommitFacts, LocalCommitInspection, LocalCommitInspection>? InspectOverride { get; set; }

    public async Task<LocalCommitIndexAcquisition?> AcquireIndexEffectsAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(AcquireIndexEffectsAsync));
        LastFacts = facts;
        if (BeforeAcquire is not null)
        {
            await BeforeAcquire(facts);
        }

        var acquisition = await inner.AcquireIndexEffectsAsync(facts, cancellationToken);
        LastAcquisition = acquisition ?? LastAcquisition;
        return acquisition;
    }

    public Task<LocalCommitIndexAcquisition?> AcquirePendingIndexEffectsAsync(
        LocalCommitFacts facts, string quarantineName, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(AcquirePendingIndexEffectsAsync));
        return inner.AcquirePendingIndexEffectsAsync(facts, quarantineName, cancellationToken);
    }

    public async Task<LocalCommitExecutionResult> PromoteRefAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(PromoteRefAsync));
        if (BeforePromoteRef is not null)
        {
            await BeforePromoteRef(facts);
        }

        var result = await inner.PromoteRefAsync(facts, acquisition, cancellationToken);
        return AfterPromoteRef is null ? result : await AfterPromoteRef(facts, result);
    }

    public async Task<LocalCommitExecutionResult> PromoteHeldIndexAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, string quarantineName, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(PromoteHeldIndexAsync));
        if (BeforePromoteHeldIndex is not null)
        {
            await BeforePromoteHeldIndex(facts);
        }

        var result = await inner.PromoteHeldIndexAsync(facts, acquisition, quarantineName, cancellationToken);
        return AfterPromoteHeldIndex is null ? result : await AfterPromoteHeldIndex(facts, result);
    }

    public Task<bool> ReleaseHeldIndexEffectsAsync(
        LocalCommitFacts facts, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ReleaseHeldIndexEffectsAsync));
        return inner.ReleaseHeldIndexEffectsAsync(facts, acquisition, cancellationToken);
    }

    public Task<LocalCommitExecutionResult> ExecuteAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ExecuteAsync));
        return inner.ExecuteAsync(facts, cancellationToken);
    }

    public async Task<LocalCommitInspection> InspectAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(InspectAsync));
        if (BeforeInspect is not null)
        {
            await BeforeInspect(facts);
        }

        var inspection = await inner.InspectAsync(facts, cancellationToken);
        return InspectOverride is null ? inspection : InspectOverride(facts, inspection);
    }

    public Task<LocalCommitExecutionResult> FinishIndexPromotionAsync(LocalCommitFacts facts, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(FinishIndexPromotionAsync));
        return inner.FinishIndexPromotionAsync(facts, cancellationToken);
    }

    public Task<bool> CleanupAsync(LocalCommitFacts facts, bool removeOwnedLock, CancellationToken cancellationToken)
    {
        Calls.Add($"{nameof(CleanupAsync)}({removeOwnedLock})");
        return inner.CleanupAsync(facts, removeOwnedLock, cancellationToken);
    }

    /// <summary>Releases the live handles this decorator saw acquired, ending the in-process capability that a simulated host
    /// loss left behind. It deletes only the lock through its own handle, exactly as the production release does.</summary>
    public async Task<bool> ReleaseRetainedAsync()
    {
        if (LastFacts is null || LastAcquisition is null)
        {
            return false;
        }

        return await inner.ReleaseHeldIndexEffectsAsync(LastFacts, LastAcquisition, CancellationToken.None);
    }
}
