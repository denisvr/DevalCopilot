using System.Diagnostics;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The one monotonic deadline of a whole entry, started as the entry's first statement. The stated outer budget covers everything the
/// entry does: argument handling, identity verification, preparation, execution, the final observations, the closure, the report, and
/// the reporting of a refusal, an error or an expiry. The last <see cref="Reserve"/> of the budget is held back for that last kind of
/// reporting, so the work, its closure and its report must be decided by the earlier <see cref="IsExpired"/> instant and the entry
/// returns no later than the budget itself: nothing is added after it. The clock is read at every query, so the deadline is correct
/// while the thread that would signal it is starved or blocked, and the waiter never depends on the work it is waiting for.
/// The entry's fate is decided once, atomically, by a small state machine: the work claims the report (<see cref="TryClaimReport"/>),
/// prints it, and only then commits (<see cref="TryCommit"/>) before persisting it; or the waiter claims the expiry
/// (<see cref="TryClaimExpiry"/>) first. Exactly one can win each step, so a report that was not committed in time is never persisted
/// as final, and a committed report is never contradicted. A write that is already inside an uncooperative sink cannot be revoked;
/// its bytes are non-final and unproven, and the caller still returns at the budget. Nothing here pre-empts a blocked operating-system
/// call or proves anything about a descendant process.
/// </summary>
public sealed class EntryDeadline
{
    private const int Running = 0;
    private const int Reporting = 1;
    private const int Committed = 2;
    private const int Expired = 3;

    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly TimeSpan _budget;
    private readonly TimeSpan _work;
    private readonly CancellationTokenSource _signal = new();
    private int _state;

    private EntryDeadline(TimeSpan budget, TimeSpan reserve)
    {
        _budget = budget;
        Reserve = reserve;
        _work = budget == Timeout.InfiniteTimeSpan ? budget : budget - reserve;
        _signal.CancelAfter(_work);
    }

    /// <summary>The part of the budget held back for reporting a refusal, an error or an expiry.</summary>
    public TimeSpan Reserve { get; }

    /// <summary>The reserve of an entry with this budget: at most five seconds and a quarter of the budget.</summary>
    public static TimeSpan ReserveFor(TimeSpan budget) =>
        budget == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(5).Ticks, budget.Ticks / 4));

    /// <summary>Starts the deadline now, with <paramref name="budget"/> in all, <paramref name="reserve"/> of it held back.</summary>
    public static EntryDeadline Start(TimeSpan budget, TimeSpan? reserve = null) => new(budget, reserve ?? ReserveFor(budget));

    /// <summary>A deadline that never elapses, for entries and offline tests that state no outer bound.</summary>
    public static EntryDeadline Unbounded() => new(Timeout.InfiniteTimeSpan, TimeSpan.Zero);

    /// <summary>Cancelled when the work deadline elapses (a convenience for awaited work; <see cref="IsExpired"/> is authoritative).</summary>
    public CancellationToken Token => _signal.Token;

    /// <summary>The time left for the work, its closure and its report.</summary>
    public TimeSpan Remaining => RemainingOf(_work);

    /// <summary>The time left in the whole stated budget, the reserve included. Every emission is bounded by it.</summary>
    public TimeSpan HardRemaining => RemainingOf(_budget);

    /// <summary>True once the work deadline elapsed or the expiry was claimed. Never reverts.</summary>
    public bool IsExpired =>
        Volatile.Read(ref _state) == Expired || (_work != Timeout.InfiniteTimeSpan && Remaining == TimeSpan.Zero);

    /// <summary>True when the expiry, not the work, decided the entry.</summary>
    public bool ExpiryClaimed => Volatile.Read(ref _state) == Expired;

    /// <summary>True once the report was printed and committed in time: the entry now only persists it.</summary>
    public bool IsCommitted => Volatile.Read(ref _state) == Committed;

    /// <summary>The work claims the right to close and report. False when the deadline has already elapsed or was claimed.</summary>
    public bool TryClaimReport() =>
        !IsExpired && Interlocked.CompareExchange(ref _state, Reporting, Running) == Running;

    /// <summary>The work commits a report it has printed, before persisting it. False when the deadline elapsed first, in which case
    /// nothing is persisted as final.</summary>
    public bool TryCommit() =>
        !IsExpired && Interlocked.CompareExchange(ref _state, Committed, Reporting) == Reporting;

    /// <summary>The waiter claims the expiry. True when the entry was not already committed; <paramref name="reportInFlight"/> says
    /// whether the work had claimed its report (so the report sink is in use and must not be written by anyone else).</summary>
    public bool TryClaimExpiry(out bool reportInFlight)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state is Committed or Expired)
            {
                reportInFlight = state == Committed;
                return state == Expired;
            }

            if (Interlocked.CompareExchange(ref _state, Expired, state) == state)
            {
                reportInFlight = state == Reporting;
                return true;
            }
        }
    }

    /// <summary>Waits for <paramref name="work"/> for at most <paramref name="limit"/>. True when it finished first. Never blocks the
    /// caller beyond the limit, whatever the work is doing.</summary>
    public static async Task<bool> WithinAsync(Task work, TimeSpan limit)
    {
        if (work.IsCompleted)
        {
            return true;
        }

        if (limit == TimeSpan.Zero)
        {
            return false;
        }

        using var timer = new CancellationTokenSource();
        var winner = await Task.WhenAny(work, Task.Delay(limit, timer.Token));
        await timer.CancelAsync();
        return winner == work;
    }

    /// <summary>Waits for <paramref name="work"/> for the time left for the work.</summary>
    public Task<bool> WithinAsync(Task work) => WithinAsync(work, Remaining);

    /// <summary>Runs one emission and waits for it for at most the time left in the whole budget. A sink that never returns cannot
    /// hold the caller, and an emission that failed or did not return in time is unproven (false). It never throws, and the emission
    /// itself is never revoked.</summary>
    public async Task<bool> EmitAsync(Func<Task> emission)
    {
        var task = Task.Run(emission);
        var returned = await WithinAsync(task, HardRemaining);
        if (returned && task.IsFaulted)
        {
            _ = task.Exception;
        }

        return returned && task.IsCompletedSuccessfully;
    }

    private TimeSpan RemainingOf(TimeSpan limit)
    {
        if (limit == Timeout.InfiniteTimeSpan)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var remaining = limit - Stopwatch.GetElapsedTime(_started);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
