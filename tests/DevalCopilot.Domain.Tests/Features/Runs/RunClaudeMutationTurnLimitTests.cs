using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class RunClaudeMutationTurnLimitTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static Run NewRun() => Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

    private static Run NewRunning()
    {
        var run = NewRun();
        run.Claim(BaseTime);
        return run;
    }

    [Fact]
    public void RequestedClaudeMaxTurns_defaults_to_null_for_a_new_run()
    {
        Assert.Null(NewRun().RequestedClaudeMaxTurns);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(37)]
    public void SetRequestedClaudeMaxTurns_accepts_valid_values_while_created_or_running(int value)
    {
        var created = NewRun();
        created.SetRequestedClaudeMaxTurns(value);
        Assert.Equal(value, created.RequestedClaudeMaxTurns);

        var running = NewRunning();
        running.SetRequestedClaudeMaxTurns(value);
        Assert.Equal(value, running.RequestedClaudeMaxTurns);
    }

    [Fact]
    public void SetRequestedClaudeMaxTurns_changes_and_clears_a_previous_request()
    {
        var run = NewRun();
        run.SetRequestedClaudeMaxTurns(5);
        run.SetRequestedClaudeMaxTurns(50);
        Assert.Equal(50, run.RequestedClaudeMaxTurns);

        run.SetRequestedClaudeMaxTurns(null);
        Assert.Null(run.RequestedClaudeMaxTurns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SetRequestedClaudeMaxTurns_rejects_out_of_range_values_and_keeps_the_prior_value(int value)
    {
        var run = NewRun();
        run.SetRequestedClaudeMaxTurns(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.SetRequestedClaudeMaxTurns(value));
        Assert.Equal(7, run.RequestedClaudeMaxTurns);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public void SetRequestedClaudeMaxTurns_rejects_every_terminal_lifecycle_and_keeps_the_prior_value(string terminal)
    {
        var run = NewRunning();
        run.SetRequestedClaudeMaxTurns(9);
        switch (terminal)
        {
            case "completed":
                run.Complete(BaseTime.AddMinutes(1));
                break;
            case "failed":
                run.Fail(BaseTime.AddMinutes(1));
                break;
            default:
                run.MarkInterrupted(BaseTime.AddMinutes(1));
                break;
        }

        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeMaxTurns(10));
        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeMaxTurns(null));
        Assert.Equal(9, run.RequestedClaudeMaxTurns);
    }

    [Fact]
    public void SetRequestedClaudeMaxTurns_does_not_change_any_other_run_field()
    {
        var run = NewRunning();
        run.SetRequestedClaudeModelRequest("opus", "high");
        run.SetRequestedCodexAssignment("gpt-6-sol", "low");
        var lifecycle = run.Lifecycle;
        var stage = run.Stage;
        var participant = run.ActiveParticipant;

        run.SetRequestedClaudeMaxTurns(20);

        Assert.Equal(lifecycle, run.Lifecycle);
        Assert.Equal(stage, run.Stage);
        Assert.Equal(participant, run.ActiveParticipant);
        Assert.Equal("opus", run.RequestedClaudeModel);
        Assert.Equal("high", run.RequestedClaudeEffort);
        Assert.Equal("gpt-6-sol", run.RequestedCodexModel);
        Assert.Equal("low", run.RequestedCodexEffort);
    }

    [Fact]
    public void The_turn_limit_event_type_has_its_stable_wire_name()
    {
        Assert.Equal("run.claude_mutation_turn_limit_changed", RunEventType.ClaudeMutationTurnLimitChanged);
    }
}
