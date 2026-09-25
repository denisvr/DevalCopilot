using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentInvocationTimeBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_newly_recorded_run_defaults_to_a_positive_one_hundred_twenty_minute_reservation_ceiling()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Bound Agent invocation time", Now);

        Assert.Equal(TimeSpan.FromMinutes(120), run.MaximumAgentInvocationTime);
        Assert.Equal(TimeSpan.FromMinutes(120), Run.DefaultMaximumAgentInvocationTime);
    }

    [Fact]
    public void Run_accepts_an_explicit_reservation_ceiling_independent_of_the_count_budget()
    {
        var run = Run.RecordIntent(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Custom time budget", Now,
            maximumAgentAttempts: 20, maximumAgentInvocationTime: TimeSpan.FromMinutes(45));

        Assert.Equal(20, run.MaximumAgentAttempts);
        Assert.Equal(TimeSpan.FromMinutes(45), run.MaximumAgentInvocationTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_explicit_reservation_ceiling_is_rejected(int invalidMinutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Run.RecordIntent(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Invalid time budget", Now,
            maximumAgentInvocationTime: TimeSpan.FromMinutes(invalidMinutes)));
    }

    [Fact]
    public void Compute_reserved_sums_every_claimed_agent_timeout()
    {
        var reserved = AgentInvocationTimeReservation.ComputeReserved(
            [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5)]);

        Assert.Equal(TimeSpan.FromMinutes(35), reserved);
    }

    [Fact]
    public void Compute_reserved_treats_an_empty_history_as_zero_already_reserved()
    {
        var reserved = AgentInvocationTimeReservation.ComputeReserved([]);

        Assert.Equal(TimeSpan.Zero, reserved);
    }

    [Fact]
    public void Compute_reserved_fails_closed_on_a_missing_agent_timeout()
    {
        var reserved = AgentInvocationTimeReservation.ComputeReserved(
            [TimeSpan.FromMinutes(10), null, TimeSpan.FromMinutes(5)]);

        Assert.Null(reserved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_reserved_fails_closed_on_a_non_positive_agent_timeout(int invalidMinutes)
    {
        var reserved = AgentInvocationTimeReservation.ComputeReserved(
            [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(invalidMinutes)]);

        Assert.Null(reserved);
    }

    [Fact]
    public void Compute_reserved_fails_closed_rather_than_throwing_when_the_aggregate_is_unrepresentable()
    {
        // Two positive, individually valid timeouts whose sum overflows TimeSpan's tick range: real,
        // positive evidence that is nonetheless not representable, not a claim of infinite time.
        var reserved = AgentInvocationTimeReservation.ComputeReserved([TimeSpan.MaxValue, TimeSpan.FromTicks(1)]);

        Assert.Null(reserved);
    }

    [Fact]
    public void Compute_projected_reservation_adds_a_candidate_timeout_to_an_already_reserved_total()
    {
        var projected = AgentInvocationTimeReservation.ComputeProjectedReservation(TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(110), projected);
    }

    [Fact]
    public void Compute_projected_reservation_fails_closed_rather_than_throwing_when_adding_the_candidate_overflows()
    {
        var projected = AgentInvocationTimeReservation.ComputeProjectedReservation(TimeSpan.MaxValue, TimeSpan.FromTicks(1));

        Assert.Null(projected);
    }
}
