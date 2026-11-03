using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R1: the one monotonic whole-entry deadline and its small state machine. It is read from the clock at every query,
/// decided once between the work and the waiter, never makes the waiter depend on the work, and keeps the emission reserve inside the
/// stated budget.</summary>
public sealed class EntryDeadlineTests
{
    [Fact]
    public void The_deadline_expires_by_the_clock_alone_without_anyone_claiming_it()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(20));
        Assert.False(deadline.IsExpired);
        Assert.True(deadline.Remaining > TimeSpan.Zero);

        Thread.Sleep(150);

        Assert.True(deadline.IsExpired);
        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
        Assert.Equal(TimeSpan.Zero, deadline.HardRemaining);
        Assert.False(deadline.ExpiryClaimed);
    }

    [Fact]
    public void The_reserve_is_inside_the_budget_so_the_work_is_decided_before_the_stated_end()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(300));

        Assert.True(deadline.HardRemaining > deadline.Remaining);
        Assert.True(deadline.HardRemaining - deadline.Remaining <= TimeSpan.FromMilliseconds(300));
        Thread.Sleep(150);
        Assert.True(deadline.IsExpired);
        Assert.True(deadline.HardRemaining > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(5), EntryDeadline.ReserveFor(TimeSpan.FromMinutes(35)));
        Assert.Equal(TimeSpan.FromMilliseconds(10), EntryDeadline.ReserveFor(TimeSpan.FromMilliseconds(40)));
    }

    [Fact]
    public async Task An_unbounded_deadline_never_expires_and_never_limits_a_wait()
    {
        var deadline = EntryDeadline.Unbounded();

        Assert.False(deadline.IsExpired);
        Assert.Equal(Timeout.InfiniteTimeSpan, deadline.Remaining);
        Assert.Equal(Timeout.InfiniteTimeSpan, deadline.HardRemaining);
        Assert.True(await deadline.WithinAsync(Task.CompletedTask));
        Assert.True(deadline.TryClaimReport());
    }

    [Fact]
    public void Exactly_one_of_the_report_claim_and_the_expiry_claim_can_win()
    {
        var reported = EntryDeadline.Unbounded();
        Assert.True(reported.TryClaimReport());
        Assert.False(reported.TryClaimReport());
        Assert.True(reported.TryClaimExpiry(out var inFlight));
        Assert.True(inFlight);
        Assert.False(reported.TryCommit());

        var expired = EntryDeadline.Unbounded();
        Assert.True(expired.TryClaimExpiry(out var running));
        Assert.False(running);
        Assert.False(expired.TryClaimReport());
        Assert.True(expired.IsExpired);
        Assert.True(expired.ExpiryClaimed);
    }

    [Fact]
    public void A_committed_report_cannot_be_expired_and_an_uncommitted_one_cannot_be_committed_after_expiry()
    {
        var committed = EntryDeadline.Unbounded();
        Assert.True(committed.TryClaimReport());
        Assert.True(committed.TryCommit());
        Assert.True(committed.IsCommitted);

        Assert.False(committed.TryClaimExpiry(out var reportInFlight));
        Assert.True(reportInFlight);
        Assert.False(committed.ExpiryClaimed);

        var late = EntryDeadline.Unbounded();
        Assert.False(late.TryCommit());
        Assert.True(late.TryClaimReport());
        Assert.True(late.TryClaimExpiry(out _));
        Assert.False(late.TryCommit());
        Assert.False(late.IsCommitted);
    }

    [Fact]
    public void A_report_cannot_be_claimed_or_committed_once_the_clock_elapsed_even_if_the_waiter_has_not_run_yet()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(10));
        Assert.True(deadline.TryClaimReport());
        Thread.Sleep(150);

        Assert.False(deadline.TryCommit());
        Assert.True(deadline.TryClaimExpiry(out var reportInFlight));
        Assert.True(reportInFlight);
    }

    [Fact]
    public async Task Waiting_returns_at_the_bound_while_the_work_is_blocked_synchronously()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(20));
        using var release = new ManualResetEventSlim();
        var work = Task.Run(() => release.Wait());
        var clock = Stopwatch.StartNew();

        var finished = await deadline.WithinAsync(work);

        Assert.False(finished);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        release.Set();
        await work;
    }

    [Fact]
    public async Task Waiting_returns_true_for_work_that_finishes_inside_the_bound()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromSeconds(30));

        Assert.True(await deadline.WithinAsync(Task.Delay(20)));
    }

    [Fact]
    public async Task An_emission_that_never_returns_cannot_hold_the_caller_past_the_stated_budget()
    {
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100));
        using var release = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();

        var completed = await deadline.EmitAsync(() => Task.Run(() => release.Wait()));

        Assert.False(completed);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(1200), "an emission waited past the stated budget");
        release.Set();
    }

    [Fact]
    public async Task An_emission_that_fails_or_finishes_reports_it_and_never_throws()
    {
        var deadline = EntryDeadline.Unbounded();

        Assert.True(await deadline.EmitAsync(() => Task.CompletedTask));
        Assert.False(await deadline.EmitAsync(() => throw new IOException("sink failed")));
    }
}
