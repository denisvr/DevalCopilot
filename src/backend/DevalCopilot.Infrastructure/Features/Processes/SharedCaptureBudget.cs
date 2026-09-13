namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// A byte budget shared between one process's stdout and stderr captures, so the two streams
/// together never exceed the request's combined cap even though each also has its own,
/// typically larger, per-stream cap. Both streams are drained concurrently on separate tasks,
/// so reservation must be atomic.
/// </summary>
internal sealed class SharedCaptureBudget(int totalBytes)
{
    private int _remaining = totalBytes;

    /// <summary>Atomically reserves up to <paramref name="requested"/> bytes from the
    /// remaining shared budget and returns how many were actually granted (0 once
    /// exhausted).</summary>
    public int Reserve(int requested)
    {
        while (true)
        {
            var current = Volatile.Read(ref _remaining);
            var granted = Math.Min(current, requested);
            if (granted <= 0)
            {
                return 0;
            }

            if (Interlocked.CompareExchange(ref _remaining, current - granted, current) == current)
            {
                return granted;
            }
        }
    }
}
