using System.Collections.Concurrent;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>Test-owned parking for completed preparations. Each completion parks until the test releases that exact capture
/// sequence, never until method entry, a timer or an assumed order. Parking is bounded; <see cref="ReleaseAll"/> is the
/// failure-safe release (it also frees completions that park afterwards) so a failed assertion cannot leave a request waiting.</summary>
internal sealed class PreparationGate(TimeSpan bound)
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
    private volatile bool _released;

    /// <summary>Parks the completion until it is released, or throws <see cref="TimeoutException"/> at the bound.</summary>
    public async Task ParkAsync(LocalCommitPreparationCompletion completion)
    {
        var gate = GateFor(completion.Sequence);
        if (_released)
        {
            gate.TrySetResult();
        }

        await gate.Task.WaitAsync(bound);
    }

    public void Release(LocalCommitPreparationCompletion completion) => GateFor(completion.Sequence).TrySetResult();

    public void ReleaseAll()
    {
        _released = true;
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }
    }

    private TaskCompletionSource GateFor(int sequence) =>
        _gates.GetOrAdd(sequence, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
