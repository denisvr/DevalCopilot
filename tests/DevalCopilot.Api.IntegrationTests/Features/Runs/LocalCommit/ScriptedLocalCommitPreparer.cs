using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>A transparent decorator over the REAL preparer whose purpose is to run a test action in the exact window between
/// the pre-admission reads/preparation and the short locked admission transaction, where a competing writer can commit. The host
/// resolves the preparer lazily, so the test creates this object first and the host attaches the real preparer to it.
/// Every result the real preparer returns is captured as a <see cref="LocalCommitPreparationCompletion"/> BEFORE any callback or
/// signal, whatever its outcome: <see cref="AfterPrepare"/> runs for a refusal as much as for a success and receives the captured
/// completion so a caller cannot assume the result was <c>Prepared</c>. Method entry signals nothing, and a call that ends without
/// returning a result (an exception) is counted in <see cref="Faulted"/> and never becomes a completion.
/// <see cref="InduceCheckpointNotCurrentFor"/> makes the REAL preparer refuse a selected call by giving it a checkpoint fingerprint
/// that cannot be current; no result is faked.</summary>
internal sealed class ScriptedLocalCommitPreparer : ILocalCommitPreparer
{
    private const string NeverCurrentFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly object _gate = new();
    private readonly List<LocalCommitPreparationCompletion> _completions = [];
    private readonly List<Waiter> _waiters = [];
    private ILocalCommitPreparer? _inner;
    private int _entered;
    private int _faulted;

    public Func<LocalCommitPreparationCompletion, Task>? AfterPrepare { get; set; }

    /// <summary>Selects, by 1-based entry ordinal, the calls whose request is made non-current so the real preparer refuses them.</summary>
    public Func<int, bool>? InduceCheckpointNotCurrentFor { get; set; }

    /// <summary>The number of calls that entered the preparer, whether or not they have returned.</summary>
    public int Entered => Volatile.Read(ref _entered);

    /// <summary>The number of calls that ended without returning a result.</summary>
    public int Faulted => Volatile.Read(ref _faulted);

    /// <summary>The captured completions so far, in capture order.</summary>
    public IReadOnlyList<LocalCommitPreparationCompletion> Completions
    {
        get
        {
            lock (_gate)
            {
                return [.. _completions];
            }
        }
    }

    public ScriptedLocalCommitPreparer Attach(ILocalCommitPreparer inner)
    {
        _inner = inner;
        return this;
    }

    /// <summary>Completes once at least <paramref name="count"/> captured completions are <c>Prepared</c> with facts; a refusal,
    /// a failure or an outstanding call never counts.</summary>
    public Task<IReadOnlyList<LocalCommitPreparationCompletion>> WhenPreparedAsync(int count) =>
        WhenAsync(count, completion => completion.IsPrepared);

    /// <summary>Completes once at least <paramref name="count"/> results have been captured, of any outcome.</summary>
    public Task<IReadOnlyList<LocalCommitPreparationCompletion>> WhenCompletedAsync(int count) => WhenAsync(count, _ => true);

    private Task<IReadOnlyList<LocalCommitPreparationCompletion>> WhenAsync(
        int count, Func<LocalCommitPreparationCompletion, bool> match)
    {
        lock (_gate)
        {
            var matched = _completions.Where(match).ToArray();
            if (matched.Length >= count)
            {
                return Task.FromResult<IReadOnlyList<LocalCommitPreparationCompletion>>(matched);
            }

            var waiter = new Waiter(count, match);
            _waiters.Add(waiter);
            return waiter.Source.Task;
        }
    }

    public async Task<LocalCommitPreparationResult> PrepareAsync(
        LocalCommitPreparationRequest request, CancellationToken cancellationToken)
    {
        var ordinal = Interlocked.Increment(ref _entered);
        var innerRequest = InduceCheckpointNotCurrentFor?.Invoke(ordinal) == true
            ? request with { CheckpointFingerprintSha256 = NeverCurrentFingerprint }
            : request;
        LocalCommitPreparationResult? result = null;
        try
        {
            result = await _inner!.PrepareAsync(innerRequest, cancellationToken);
        }
        finally
        {
            if (result is null)
            {
                Interlocked.Increment(ref _faulted);
            }
        }

        var completion = Capture(innerRequest, result);
        if (AfterPrepare is not null)
        {
            await AfterPrepare(completion);
        }

        return result;
    }

    private LocalCommitPreparationCompletion Capture(LocalCommitPreparationRequest request, LocalCommitPreparationResult result)
    {
        LocalCommitPreparationCompletion completion;
        List<Waiter> satisfied;
        lock (_gate)
        {
            completion = new LocalCommitPreparationCompletion(_completions.Count + 1, request, result);
            _completions.Add(completion);
            satisfied = _waiters.Where(waiter => _completions.Count(waiter.Match) >= waiter.Count).ToList();
            foreach (var waiter in satisfied)
            {
                _waiters.Remove(waiter);
            }
        }

        foreach (var waiter in satisfied)
        {
            waiter.Source.TrySetResult(Completions.Where(waiter.Match).ToArray());
        }

        return completion;
    }

    private sealed class Waiter(int count, Func<LocalCommitPreparationCompletion, bool> match)
    {
        public int Count { get; } = count;

        public Func<LocalCommitPreparationCompletion, bool> Match { get; } = match;

        public TaskCompletionSource<IReadOnlyList<LocalCommitPreparationCompletion>> Source { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
