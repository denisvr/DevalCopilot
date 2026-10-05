using System.Reflection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The advisory Codex account-usage warning value (ADR-0026): its bounds, canonical stored form, strict reading of every
/// other stored representation and its Run lifecycle guard. A stored value that is not exactly the canonical digits of a whole number
/// from 1 to 100 is malformed, never coerced.</summary>
public sealed class CodexAccountUsageWarningTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static Run NewRun() => Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Warn at an account percentage", BaseTime);

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
    public void Validity_is_one_through_one_hundred_or_cleared(int? value, bool valid)
    {
        Assert.Equal(valid, CodexAccountUsageWarning.IsValid(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, "1")]
    [InlineData(80, "80")]
    [InlineData(100, "100")]
    public void Format_is_the_canonical_digits_or_null(int? value, string? expected)
    {
        Assert.Equal(expected, CodexAccountUsageWarning.Format(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Format_refuses_a_value_out_of_range(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexAccountUsageWarning.Format(value));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("9", 9)]
    [InlineData("10", 10)]
    [InlineData("99", 99)]
    [InlineData("100", 100)]
    public void Read_accepts_exactly_the_canonical_digits(string stored, int expected)
    {
        var reading = CodexAccountUsageWarning.Read(stored);

        Assert.False(reading.IsMalformed);
        Assert.Equal(expected, reading.Value);
    }

    [Fact]
    public void Read_of_null_is_absent_and_not_malformed()
    {
        var reading = CodexAccountUsageWarning.Read(null);

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
        var reading = CodexAccountUsageWarning.Read(stored);

        Assert.True(reading.IsMalformed);
        Assert.False(reading.IsAbsent);
        Assert.Null(reading.Value);
    }

    [Fact]
    public void A_new_run_has_no_warning_and_it_is_settable_changeable_and_clearable_while_created_or_running()
    {
        var created = NewRun();
        Assert.True(created.ReadCodexAccountUsageWarningPercent().IsAbsent);

        created.SetCodexAccountUsageWarningPercent(80);
        Assert.Equal(80, created.ReadCodexAccountUsageWarningPercent().Value);

        var running = NewRun();
        running.Claim(BaseTime);
        running.SetCodexAccountUsageWarningPercent(1);
        running.SetCodexAccountUsageWarningPercent(100);
        Assert.Equal(100, running.ReadCodexAccountUsageWarningPercent().Value);

        running.SetCodexAccountUsageWarningPercent(null);
        Assert.True(running.ReadCodexAccountUsageWarningPercent().IsAbsent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void An_invalid_value_is_refused_and_keeps_the_prior_setting(int value)
    {
        var run = NewRun();
        run.SetCodexAccountUsageWarningPercent(60);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.SetCodexAccountUsageWarningPercent(value));

        Assert.Equal(60, run.ReadCodexAccountUsageWarningPercent().Value);
    }

    [Fact]
    public void A_terminal_run_refuses_a_change_and_keeps_its_setting()
    {
        var run = NewRun();
        run.Claim(BaseTime);
        run.SetCodexAccountUsageWarningPercent(70);
        run.Fail(BaseTime);

        Assert.Throws<InvalidOperationException>(() => run.SetCodexAccountUsageWarningPercent(null));

        Assert.Equal(70, run.ReadCodexAccountUsageWarningPercent().Value);
    }

    [Fact]
    public void A_valid_set_or_clear_repairs_a_malformed_stored_value_and_never_reads_it_as_a_number()
    {
        var run = NewRun();
        SetStored(run, "_codexAccountUsageWarningPercent", "t:80");
        Assert.True(run.ReadCodexAccountUsageWarningPercent().IsMalformed);

        run.SetCodexAccountUsageWarningPercent(null);
        Assert.True(run.ReadCodexAccountUsageWarningPercent().IsAbsent);

        SetStored(run, "_codexAccountUsageWarningPercent", "r:4014000000000000");
        run.SetCodexAccountUsageWarningPercent(40);
        Assert.Equal(40, run.ReadCodexAccountUsageWarningPercent().Value);
    }

    [Fact]
    public void The_warning_and_the_stop_are_independent_with_no_required_ordering()
    {
        var run = NewRun();

        run.SetCodexAccountUsageStopPercent(30);
        run.SetCodexAccountUsageWarningPercent(90);
        Assert.Equal(30, run.ReadCodexAccountUsageStopPercent().Value);
        Assert.Equal(90, run.ReadCodexAccountUsageWarningPercent().Value);

        run.SetCodexAccountUsageWarningPercent(null);
        Assert.Equal(30, run.ReadCodexAccountUsageStopPercent().Value);

        run.SetCodexAccountUsageStopPercent(null);
        run.SetCodexAccountUsageWarningPercent(10);
        Assert.True(run.ReadCodexAccountUsageStopPercent().IsAbsent);
        Assert.Equal(10, run.ReadCodexAccountUsageWarningPercent().Value);
    }

    [Fact]
    public void The_event_type_has_its_stable_wire_name()
    {
        Assert.Equal("run.codex_account_usage_warning_changed", RunEventType.CodexAccountUsageWarningChanged);
    }
}
