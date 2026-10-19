using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentSupervisionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static AgentSupervision Open() => AgentSupervision.Register(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start);

    [Fact]
    public void Registration_records_the_exact_identity_in_Preparing_at_revision_one_with_a_two_minute_deadline()
    {
        var id = Guid.NewGuid();
        var run = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var host = Guid.NewGuid();
        var observer = Guid.NewGuid();

        var receipt = AgentSupervision.Register(id, run, attempt, host, observer, Start);

        Assert.Equal((id, run, attempt, host, observer), (receipt.Id, receipt.RunId, receipt.AttemptId, receipt.HostInstanceId, receipt.ObserverId));
        Assert.Equal(AgentSupervisionPhase.Preparing, receipt.Phase);
        Assert.Equal(1, receipt.Revision);
        Assert.Equal(Start, receipt.StartedAtUtc);
        Assert.Equal(Start, receipt.LastHeartbeatAtUtc);
        Assert.Equal(Start, receipt.PhaseEnteredAtUtc);
        Assert.Equal(Start.AddMinutes(2), receipt.PhaseDeadlineUtc);
        Assert.Null(receipt.AttentionReason);
        Assert.Null(receipt.ClosureReason);
        Assert.True(receipt.IsOpen);
    }

    [Fact]
    public void Registration_rejects_an_empty_identifier_and_a_default_time()
    {
        Assert.Throws<ArgumentException>(() => AgentSupervision.Register(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start));
        Assert.Throws<ArgumentException>(() => AgentSupervision.Register(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentSupervision.Register(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public void Ownership_requires_both_the_original_host_and_the_original_observer_and_an_identical_registration_replays()
    {
        var receipt = Open();

        Assert.True(receipt.IsOwnedBy(receipt.HostInstanceId, receipt.ObserverId));
        Assert.False(receipt.IsOwnedBy(Guid.NewGuid(), receipt.ObserverId));
        Assert.False(receipt.IsOwnedBy(receipt.HostInstanceId, Guid.NewGuid()));
        Assert.True(receipt.IsIdenticalRegistration(receipt.RunId, receipt.AttemptId, receipt.HostInstanceId, receipt.ObserverId));
        Assert.False(receipt.IsIdenticalRegistration(Guid.NewGuid(), receipt.AttemptId, receipt.HostInstanceId, receipt.ObserverId));
        Assert.False(receipt.IsIdenticalRegistration(receipt.RunId, receipt.AttemptId, receipt.HostInstanceId, Guid.NewGuid()));
    }

    [Fact]
    public void A_heartbeat_advances_the_revision_and_never_goes_backward()
    {
        var receipt = Open();

        receipt.RecordHeartbeat(Start.AddSeconds(5));
        receipt.RecordHeartbeat(Start.AddSeconds(5));

        Assert.Equal(3, receipt.Revision);
        Assert.Equal(Start.AddSeconds(5), receipt.LastHeartbeatAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.RecordHeartbeat(Start.AddSeconds(4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.RecordHeartbeat(default));
    }

    [Fact]
    public void Phases_move_forward_only_once_and_may_skip_a_phase()
    {
        var receipt = Open();
        receipt.AdvancePhase(AgentSupervisionPhase.AwaitingAdapterResult, Start.AddSeconds(1), Start.AddSeconds(1).AddMinutes(12));

        Assert.Throws<InvalidOperationException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.AwaitingAdapterResult, Start.AddSeconds(2), Start.AddSeconds(2).AddMinutes(12)));
        Assert.Throws<InvalidOperationException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.Preparing, Start.AddSeconds(2), Start.AddSeconds(2).AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => receipt.AdvancePhase((AgentSupervisionPhase)99, Start.AddSeconds(2), Start.AddMinutes(9)));

        receipt.AdvancePhase(AgentSupervisionPhase.RecordingResult, Start.AddSeconds(3), Start.AddSeconds(3).AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.RecordingResult, Start.AddSeconds(4), Start.AddSeconds(4).AddMinutes(2)));

        var skipper = Open();
        skipper.AdvancePhase(AgentSupervisionPhase.RecordingResult, Start.AddSeconds(1), Start.AddSeconds(1).AddMinutes(2));
        Assert.Equal(AgentSupervisionPhase.RecordingResult, skipper.Phase);
    }

    [Fact]
    public void A_phase_entry_fixes_its_own_absolute_deadline_and_counts_as_a_report()
    {
        var receipt = Open();

        receipt.AdvancePhase(AgentSupervisionPhase.AwaitingAdapterResult, Start.AddSeconds(7), Start.AddSeconds(7).AddMinutes(12));

        Assert.Equal(Start.AddSeconds(7), receipt.PhaseEnteredAtUtc);
        Assert.Equal(Start.AddSeconds(7).AddMinutes(12), receipt.PhaseDeadlineUtc);
        Assert.Equal(Start.AddSeconds(7), receipt.LastHeartbeatAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.RecordingResult, Start.AddSeconds(8), Start.AddSeconds(8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.RecordingResult, Start.AddSeconds(6), Start.AddMinutes(9)));
    }

    [Fact]
    public void The_first_attention_is_retained_and_a_second_is_ignored()
    {
        var receipt = Open();

        Assert.True(receipt.RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Start.AddSeconds(30)));
        var revision = receipt.Revision;
        Assert.False(receipt.RecordAttention(AgentSupervisionAttentionReason.HostInterrupted, Start.AddSeconds(31)));

        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
        Assert.Equal(Start.AddSeconds(30), receipt.AttentionAtUtc);
        Assert.Equal(revision, receipt.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("STALE_HEARTBEAT")]
    [InlineData("anything_else")]
    public void Attention_accepts_only_the_fixed_reasons(string reason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Open().RecordAttention(reason, Start));
    }

    [Fact]
    public void Attention_before_the_observation_started_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Open().RecordAttention(AgentSupervisionAttentionReason.StaleHeartbeat, Start.AddTicks(-1)));
    }

    [Fact]
    public void Closing_keeps_the_first_attention_and_a_closed_observation_never_changes_again()
    {
        var receipt = Open();
        receipt.RecordAttention(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, Start.AddMinutes(2));
        receipt.RecordHeartbeat(Start.AddMinutes(2));

        receipt.Close(AgentSupervisionClosureReason.ObserverEnded, Start.AddMinutes(3));

        Assert.False(receipt.IsOpen);
        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, receipt.ClosureReason);
        Assert.Equal(Start.AddMinutes(3), receipt.ClosedAtUtc);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, receipt.AttentionReason);
        Assert.Throws<InvalidOperationException>(() => receipt.RecordHeartbeat(Start.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() => receipt.RecordAttention(AgentSupervisionAttentionReason.HostInterrupted, Start.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() => receipt.AdvancePhase(
            AgentSupervisionPhase.RecordingResult, Start.AddMinutes(4), Start.AddMinutes(9)));
        Assert.Throws<InvalidOperationException>(() => receipt.Close(AgentSupervisionClosureReason.AttemptTerminal, Start.AddMinutes(4)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ATTEMPT_TERMINAL")]
    [InlineData("stale_heartbeat")]
    public void Closure_accepts_only_the_fixed_reasons(string reason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Open().Close(reason, Start));
    }

    [Fact]
    public void Closing_before_the_last_report_is_refused()
    {
        var receipt = Open();
        receipt.RecordHeartbeat(Start.AddSeconds(10));

        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.Close(AgentSupervisionClosureReason.AttemptTerminal, Start.AddSeconds(9)));
        Assert.True(receipt.IsOpen);
    }

    [Fact]
    public void The_reason_vocabularies_are_closed()
    {
        Assert.True(AgentSupervisionAttentionReason.IsKnown("stale_heartbeat"));
        Assert.True(AgentSupervisionAttentionReason.IsKnown("phase_deadline_elapsed"));
        Assert.True(AgentSupervisionAttentionReason.IsKnown("observer_ended_without_terminal_result"));
        Assert.True(AgentSupervisionAttentionReason.IsKnown("host_interrupted"));
        Assert.False(AgentSupervisionAttentionReason.IsKnown(null));
        Assert.True(AgentSupervisionClosureReason.IsKnown("attempt_terminal"));
        Assert.True(AgentSupervisionClosureReason.IsKnown("observer_ended"));
        Assert.True(AgentSupervisionClosureReason.IsKnown("host_interrupted"));
        Assert.False(AgentSupervisionClosureReason.IsKnown("stale_heartbeat"));
    }
}
