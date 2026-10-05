using System.Reflection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The Codex account-usage stop value (ADR-0025): its bounds, canonical stored form, strict reading of every other stored
/// representation, and the immutable per-attempt snapshot. A stored value that is not exactly the canonical digits of a whole
/// number from 1 to 100 is malformed, never coerced.</summary>
public sealed class CodexAccountUsageStopTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static Run NewRun() => Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Stop at an account percentage", BaseTime);

    private static Attempt ClaimCodex() => Attempt.ClaimAgentWithAssignment(
        Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, Guid.NewGuid(), Guid.NewGuid(),
        "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime,
        requestedModel: null, requestedEffort: null, agentBudgetSlot: 1);

    private static void SetStored(object target, string field, string? value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(50, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MinValue, false)]
    public void Validity_is_one_through_one_hundred_or_disabled(int? value, bool valid)
    {
        Assert.Equal(valid, CodexAccountUsageStop.IsValid(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, "1")]
    [InlineData(80, "80")]
    [InlineData(100, "100")]
    public void Format_is_the_canonical_digits_or_null(int? value, string? expected)
    {
        Assert.Equal(expected, CodexAccountUsageStop.Format(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Format_refuses_a_value_out_of_range(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexAccountUsageStop.Format(value));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("9", 9)]
    [InlineData("10", 10)]
    [InlineData("99", 99)]
    [InlineData("100", 100)]
    public void Read_accepts_exactly_the_canonical_digits(string stored, int expected)
    {
        var reading = CodexAccountUsageStop.Read(stored);

        Assert.False(reading.IsMalformed);
        Assert.Equal(expected, reading.Value);
    }

    [Fact]
    public void Read_of_null_is_absent_and_not_malformed()
    {
        var reading = CodexAccountUsageStop.Read(null);

        Assert.True(reading.IsAbsent);
        Assert.False(reading.IsMalformed);
        Assert.Null(reading.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("101")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("5.0")]
    [InlineData("5.5")]
    [InlineData("1e1")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("٥")]
    [InlineData("r:4014000000000000")]
    [InlineData("t:80")]
    [InlineData("b:3830")]
    [InlineData("80%")]
    public void Read_marks_every_other_stored_representation_malformed(string stored)
    {
        var reading = CodexAccountUsageStop.Read(stored);

        Assert.True(reading.IsMalformed);
        Assert.False(reading.IsAbsent);
        Assert.Null(reading.Value);
    }

    [Fact]
    public void A_new_run_has_no_stop_and_it_is_settable_changeable_and_clearable_while_created_or_running()
    {
        var created = NewRun();
        Assert.True(created.ReadCodexAccountUsageStopPercent().IsAbsent);

        created.SetCodexAccountUsageStopPercent(80);
        Assert.Equal(80, created.ReadCodexAccountUsageStopPercent().Value);

        var running = NewRun();
        running.Claim(BaseTime);
        running.SetCodexAccountUsageStopPercent(1);
        running.SetCodexAccountUsageStopPercent(100);
        Assert.Equal(100, running.ReadCodexAccountUsageStopPercent().Value);

        running.SetCodexAccountUsageStopPercent(null);
        Assert.True(running.ReadCodexAccountUsageStopPercent().IsAbsent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void An_invalid_value_is_refused_and_keeps_the_prior_setting(int value)
    {
        var run = NewRun();
        run.SetCodexAccountUsageStopPercent(60);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.SetCodexAccountUsageStopPercent(value));

        Assert.Equal(60, run.ReadCodexAccountUsageStopPercent().Value);
    }

    [Fact]
    public void A_terminal_run_refuses_a_change_and_keeps_its_setting()
    {
        var run = NewRun();
        run.Claim(BaseTime);
        run.SetCodexAccountUsageStopPercent(70);
        run.Fail(BaseTime);

        Assert.Throws<InvalidOperationException>(() => run.SetCodexAccountUsageStopPercent(null));

        Assert.Equal(70, run.ReadCodexAccountUsageStopPercent().Value);
    }

    [Fact]
    public void A_valid_set_or_clear_repairs_a_malformed_stored_value_and_never_reads_it_as_a_number()
    {
        var run = NewRun();
        SetStored(run, "_codexAccountUsageStopPercent", "t:80");
        Assert.True(run.ReadCodexAccountUsageStopPercent().IsMalformed);

        run.SetCodexAccountUsageStopPercent(null);
        Assert.True(run.ReadCodexAccountUsageStopPercent().IsAbsent);

        SetStored(run, "_codexAccountUsageStopPercent", "r:4014000000000000");
        run.SetCodexAccountUsageStopPercent(40);
        Assert.Equal(40, run.ReadCodexAccountUsageStopPercent().Value);
    }

    [Fact]
    public void The_event_type_has_its_stable_wire_name()
    {
        Assert.Equal("run.codex_account_usage_stop_changed", RunEventType.CodexAccountUsageStopChanged);
    }

    [Fact]
    public void An_attempt_is_claimed_without_a_snapshot_and_takes_one_exactly_once_before_dispatch()
    {
        var attempt = ClaimCodex();
        Assert.True(attempt.ReadAgentCodexAccountUsageStopPercent().IsAbsent);

        attempt.SnapshotCodexAccountUsageStop(85);

        Assert.Equal(85, attempt.ReadAgentCodexAccountUsageStopPercent().Value);
        Assert.Throws<InvalidOperationException>(() => attempt.SnapshotCodexAccountUsageStop(90));
        Assert.Equal(85, attempt.ReadAgentCodexAccountUsageStopPercent().Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void The_snapshot_refuses_an_invalid_value(int value)
    {
        var attempt = ClaimCodex();

        Assert.Throws<ArgumentOutOfRangeException>(() => attempt.SnapshotCodexAccountUsageStop(value));

        Assert.True(attempt.ReadAgentCodexAccountUsageStopPercent().IsAbsent);
    }

    [Fact]
    public void The_snapshot_is_refused_after_dispatch_and_after_completion()
    {
        var dispatched = ClaimCodex();
        dispatched.MarkAgentDispatched(BaseTime);
        Assert.Throws<InvalidOperationException>(() => dispatched.SnapshotCodexAccountUsageStop(50));

        var completed = ClaimCodex();
        completed.CompleteAgent(AgentOutcome.WorkspaceNoLongerEligible, null, BaseTime);
        Assert.Throws<InvalidOperationException>(() => completed.SnapshotCodexAccountUsageStop(50));
    }

    [Fact]
    public void A_non_agent_attempt_cannot_take_the_snapshot()
    {
        var process = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Throws<InvalidOperationException>(() => process.SnapshotCodexAccountUsageStop(50));
    }

    [Fact]
    public void A_tampered_attempt_snapshot_reads_malformed_without_throwing()
    {
        var attempt = ClaimCodex();
        SetStored(attempt, "_agentCodexAccountUsageStopPercent", "5.5");

        Assert.True(attempt.ReadAgentCodexAccountUsageStopPercent().IsMalformed);
    }
}
