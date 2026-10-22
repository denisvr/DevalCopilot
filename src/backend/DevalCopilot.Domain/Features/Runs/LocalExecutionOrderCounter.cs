namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one feature-owned counter that gives every coordination and admission number its stable, strictly increasing Int64 order
/// (ADR-0062). It is advanced only under the database write lock, never wraps and never coerces a malformed value: an exhausted or
/// negative counter makes the allocation fail closed instead of reusing an order.
/// </summary>
public sealed class LocalExecutionOrderCounter
{
    public const int SingletonId = 1;

    private LocalExecutionOrderCounter()
    {
    }

    public int Id { get; private set; }

    /// <summary>The last number handed out. Zero before any allocation.</summary>
    public long LastNumber { get; private set; }

    public long Revision { get; private set; }

    /// <summary>The next order number, strictly greater than every earlier one. Throws when the counter is malformed or exhausted.</summary>
    public long Allocate()
    {
        if (LastNumber < 0 || LastNumber == long.MaxValue || Id != SingletonId)
        {
            throw new InvalidOperationException("The local execution order counter cannot allocate another number.");
        }

        LastNumber++;
        Revision++;
        return LastNumber;
    }
}
