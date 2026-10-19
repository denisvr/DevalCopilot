using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentSupervisionPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static AgentSupervision Open() => AgentSupervision.Register(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start);

    [Fact]
    public void The_timing_constants_are_the_adr_values()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), AgentSupervisionPolicy.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), AgentSupervisionPolicy.StaleAfter);
        Assert.Equal(TimeSpan.FromMinutes(2), AgentSupervisionPolicy.PreparingDeadline);
        Assert.Equal(TimeSpan.FromMinutes(2), AgentSupervisionPolicy.RecordingResultDeadline);
        Assert.Equal(TimeSpan.FromMinutes(2), AgentSupervisionPolicy.AdapterResultGrace);
        Assert.Equal(TimeSpan.FromSeconds(2), AgentSupervisionPolicy.CallBound);
    }

    [Fact]
    public void Phase_deadlines_are_entry_plus_the_fixed_span_and_the_adapter_phase_uses_the_actual_reserved_timeout()
    {
        Assert.Equal(Start.AddMinutes(2), AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.Preparing, Start, null));
        Assert.Equal(Start.AddMinutes(2), AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.RecordingResult, Start, null));
        Assert.Equal(
            Start.AddMinutes(12),
            AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.AwaitingAdapterResult, Start, TimeSpan.FromMinutes(10)));
        Assert.Equal(
            Start.AddMinutes(7),
            AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.AwaitingAdapterResult, Start, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void The_adapter_phase_never_fabricates_a_deadline_without_a_positive_reserved_timeout(long? ticks)
    {
        TimeSpan? timeout = ticks is { } value ? TimeSpan.FromTicks(value) : null;

        Assert.Null(AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.AwaitingAdapterResult, Start, timeout));
    }

    [Fact]
    public void A_deadline_that_overflows_is_refused_not_wrapped()
    {
        Assert.Null(AgentSupervisionPolicy.PhaseDeadlineOf(
            AgentSupervisionPhase.AwaitingAdapterResult, Start, TimeSpan.FromTicks(long.MaxValue)));
        Assert.Null(AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.Preparing, DateTimeOffset.MaxValue.AddSeconds(-1), null));
        Assert.Null(AgentSupervisionPolicy.StaleAtOf(DateTimeOffset.MaxValue.AddSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentSupervision.Register(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.MaxValue.AddSeconds(-1)));
    }

    [Fact]
    public void An_undefined_phase_has_no_rank_and_no_deadline()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentSupervisionPolicy.Rank((AgentSupervisionPhase)99));
        Assert.Null(AgentSupervisionPolicy.PhaseDeadlineOf((AgentSupervisionPhase)99, Start, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(29_999, false)]
    [InlineData(30_000, true)]
    [InlineData(30_001, true)]
    public void Staleness_is_exactly_thirty_seconds_since_the_last_committed_heartbeat_and_the_boundary_is_expired(
        long milliseconds, bool stale)
    {
        var receipt = Open();

        var assessment = AgentSupervisionPolicy.Assess(receipt, Start.AddMilliseconds(milliseconds), ownedByCurrentHost: true);

        Assert.True(assessment.ChronologyValid);
        Assert.Equal(stale ? AgentSupervisionAttentionReason.StaleHeartbeat : null, assessment.EffectiveReason);
        Assert.False(assessment.Recorded);
        if (stale)
        {
            Assert.Equal(Start.AddSeconds(30), assessment.EffectiveAtUtc);
        }
    }

    [Fact]
    public void The_phase_deadline_boundary_is_expired_even_with_a_fresh_heartbeat()
    {
        var receipt = Open();
        // A heartbeat one second before the deadline: fresh, yet the absolute deadline still elapses and is never masked.
        receipt.RecordHeartbeat(Start.AddSeconds(119));

        var before = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(119).AddTicks(9_999_999), ownedByCurrentHost: true);
        var exact = AgentSupervisionPolicy.Assess(receipt, Start.AddMinutes(2), ownedByCurrentHost: true);

        Assert.Null(before.EffectiveReason);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, exact.EffectiveReason);
        Assert.Equal(Start.AddMinutes(2), exact.EffectiveAtUtc);
    }

    [Fact]
    public void A_heartbeat_never_moves_the_phase_deadline()
    {
        var receipt = Open();
        var deadline = receipt.PhaseDeadlineUtc;

        receipt.RecordHeartbeat(Start.AddSeconds(5));
        receipt.RecordHeartbeat(Start.AddSeconds(10));

        Assert.Equal(deadline, receipt.PhaseDeadlineUtc);
        Assert.Equal(Start, receipt.PhaseEnteredAtUtc);
        Assert.Equal(AgentSupervisionPhase.Preparing, receipt.Phase);
    }

    [Fact]
    public void The_earlier_of_the_two_boundaries_is_the_effective_attention_and_the_deadline_wins_a_tie()
    {
        var stale = Open();
        Assert.Equal(
            AgentSupervisionAttentionReason.StaleHeartbeat,
            AgentSupervisionPolicy.Assess(stale, Start.AddMinutes(5), ownedByCurrentHost: true).EffectiveReason);

        // Heartbeat at +90s: stale at +120s, the very instant the deadline elapses; the deadline wins the tie.
        var tie = Open();
        tie.RecordHeartbeat(Start.AddSeconds(90));
        var assessment = AgentSupervisionPolicy.Assess(tie, Start.AddMinutes(2), ownedByCurrentHost: true);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, assessment.EffectiveReason);

        // A late phase whose deadline lies far after the staleness boundary: staleness comes first.
        var longPhase = Open();
        longPhase.AdvancePhase(
            AgentSupervisionPhase.AwaitingAdapterResult, Start.AddSeconds(1), Start.AddSeconds(1).AddMinutes(12));
        Assert.Equal(
            AgentSupervisionAttentionReason.StaleHeartbeat,
            AgentSupervisionPolicy.Assess(longPhase, Start.AddMinutes(5), ownedByCurrentHost: true).EffectiveReason);
        Assert.Equal(
            AgentSupervisionAttentionReason.PhaseDeadlineElapsed,
            AgentSupervisionPolicy.Assess(
                BeatingThrough(Start.AddSeconds(1), Start.AddMinutes(13)), Start.AddMinutes(13), ownedByCurrentHost: true).EffectiveReason);
    }

    private static AgentSupervision BeatingThrough(DateTimeOffset from, DateTimeOffset until)
    {
        var receipt = Open();
        receipt.AdvancePhase(AgentSupervisionPhase.AwaitingAdapterResult, from, from.AddMinutes(12));
        for (var at = from.AddSeconds(5); at <= until.AddSeconds(-1); at = at.AddSeconds(5))
        {
            receipt.RecordHeartbeat(at);
        }

        return receipt;
    }

    [Fact]
    public void A_recorded_first_attention_is_effective_and_never_replaced_by_a_later_phase_or_heartbeat()
    {
        var receipt = Open();
        Assert.True(receipt.RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Start.AddSeconds(30)));

        receipt.RecordHeartbeat(Start.AddSeconds(40));
        receipt.AdvancePhase(AgentSupervisionPhase.RecordingResult, Start.AddSeconds(41), Start.AddSeconds(41).AddMinutes(2));
        Assert.False(receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, Start.AddSeconds(50)));

        var assessment = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(42), ownedByCurrentHost: true);

        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, assessment.EffectiveReason);
        Assert.Equal(Start.AddSeconds(30), assessment.EffectiveAtUtc);
        Assert.True(assessment.Recorded);
    }

    [Fact]
    public void A_future_heartbeat_or_a_backward_clock_is_unavailable_never_fresh()
    {
        var receipt = Open();
        receipt.RecordHeartbeat(Start.AddSeconds(10));

        var backward = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(9), ownedByCurrentHost: true);

        Assert.False(backward.ChronologyValid);
        Assert.Null(backward.EffectiveReason);
        Assert.False(AgentSupervisionPolicy.Assess(receipt, default, ownedByCurrentHost: true).ChronologyValid);
    }

    [Fact]
    public void A_recorded_first_attention_later_than_the_clock_is_unavailable_never_needs_attention()
    {
        // ASUP-R3: the sweep recorded the stale boundary, then the host clock moved back before it. The recorded fact is impossible now.
        var receipt = Open();
        Assert.True(receipt.RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Start.AddSeconds(30)));

        var rolledBack = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(20), ownedByCurrentHost: true);
        var exactBoundary = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(30), ownedByCurrentHost: true);

        Assert.False(rolledBack.ChronologyValid);
        Assert.Null(rolledBack.EffectiveReason);
        Assert.True(exactBoundary.ChronologyValid);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, exactBoundary.EffectiveReason);
        Assert.True(exactBoundary.Recorded);
    }

    [Fact]
    public void A_closed_observation_whose_recorded_attention_is_later_than_the_clock_is_unavailable()
    {
        var receipt = Open();
        Assert.True(receipt.RecordAttention(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, Start.AddSeconds(8)));
        receipt.Close(AgentSupervisionClosureReason.ObserverEnded, Start.AddSeconds(4));

        Assert.False(AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(6), ownedByCurrentHost: true).ChronologyValid);
        Assert.True(AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(8), ownedByCurrentHost: true).ChronologyValid);
    }

    [Fact]
    public void An_old_hosts_freshness_is_never_accepted_before_persistence()
    {
        var receipt = Open();

        var assessment = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(1), ownedByCurrentHost: false);

        Assert.Equal(AgentSupervisionAttentionReason.HostInterrupted, assessment.EffectiveReason);
        Assert.Equal(Start.AddSeconds(1), assessment.EffectiveAtUtc);
    }

    [Fact]
    public void A_closed_observation_without_attention_has_none_and_a_closure_in_the_future_is_unavailable()
    {
        var receipt = Open();
        receipt.Close(AgentSupervisionClosureReason.AttemptTerminal, Start.AddSeconds(3));

        var settled = AgentSupervisionPolicy.Assess(receipt, Start.AddMinutes(10), ownedByCurrentHost: true);
        var early = AgentSupervisionPolicy.Assess(receipt, Start.AddSeconds(2), ownedByCurrentHost: true);

        Assert.True(settled.ChronologyValid);
        Assert.Null(settled.EffectiveReason);
        Assert.False(early.ChronologyValid);
    }

    [Fact]
    public void A_terminal_result_is_judged_at_its_own_completion_time()
    {
        // The observer kept reporting until just before the deadline, so only the deadline can decide a late result.
        var receipt = Open();
        for (var at = Start.AddSeconds(5); at < Start.AddMinutes(2); at = at.AddSeconds(5))
        {
            receipt.RecordHeartbeat(at);
        }

        var timely = AgentSupervisionPolicy.AssessAt(receipt, Start.AddMinutes(2).AddTicks(-1));
        var boundary = AgentSupervisionPolicy.AssessAt(receipt, Start.AddMinutes(2));
        var beforeStart = AgentSupervisionPolicy.AssessAt(receipt, Start.AddTicks(-1));

        Assert.True(timely.ChronologyValid);
        Assert.Null(timely.EffectiveReason);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, boundary.EffectiveReason);
        Assert.Equal(Start.AddMinutes(2), boundary.EffectiveAtUtc);
        Assert.False(beforeStart.ChronologyValid);
    }

    [Fact]
    public void A_result_after_a_silent_observer_is_judged_stale_at_its_own_completion_time()
    {
        var receipt = Open();

        Assert.Null(AgentSupervisionPolicy.AssessAt(receipt, Start.AddSeconds(29)).EffectiveReason);
        Assert.Equal(
            AgentSupervisionAttentionReason.StaleHeartbeat, AgentSupervisionPolicy.AssessAt(receipt, Start.AddSeconds(30)).EffectiveReason);
    }

    [Fact]
    public void A_heartbeat_after_the_completion_proves_the_observer_was_reporting_then()
    {
        var receipt = Open();
        receipt.RecordHeartbeat(Start.AddSeconds(100));

        // Completed at +40s: the last heartbeat (+100s) was committed after it, so it is not stale at completion.
        Assert.Null(AgentSupervisionPolicy.AssessAt(receipt, Start.AddSeconds(40)).EffectiveReason);
    }

    [Fact]
    public void Coherent_stored_times_stay_coherent_through_every_transition()
    {
        var receipt = Open();
        Assert.True(AgentSupervisionPolicy.IsCoherent(receipt));

        receipt.RecordHeartbeat(Start.AddSeconds(5));
        receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, Start.AddSeconds(6));
        receipt.AdvancePhase(AgentSupervisionPhase.RecordingResult, Start.AddSeconds(7), Start.AddSeconds(7).AddMinutes(2));
        receipt.Close(AgentSupervisionClosureReason.ObserverEnded, Start.AddSeconds(8));

        Assert.True(AgentSupervisionPolicy.IsCoherent(receipt));
    }
}
