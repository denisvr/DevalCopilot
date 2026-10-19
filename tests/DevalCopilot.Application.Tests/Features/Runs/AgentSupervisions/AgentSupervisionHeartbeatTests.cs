using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 heartbeats and phases: ownership fences, forward-only phases with absolute deadlines, exact boundaries, a fresh
/// heartbeat that cannot mask a hung phase, first attention recorded before a late writer can erase a gap, and clock refusal.</summary>
public sealed class AgentSupervisionHeartbeatTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(AgentSupervisionTestScene Scene, Guid AttemptId, Guid Observer)> ObservedAsync(TimeSpan? timeout = null)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync(timeout: timeout);
        return (scene, attemptId, await scene.ObserveAttemptAsync(attemptId));
    }

    [Fact]
    public async Task A_heartbeat_records_the_report_without_moving_the_phase_its_deadline_or_the_timeline()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        var before = await scene.ReceiptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(5));

        var result = await scene.HeartbeatAsync(attemptId, observer);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AttentionRecorded);
        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(5), after.LastHeartbeatAtUtc);
        Assert.Equal(before.PhaseDeadlineUtc, after.PhaseDeadlineUtc);
        Assert.Equal(before.PhaseEnteredAtUtc, after.PhaseEnteredAtUtc);
        Assert.Equal(AgentSupervisionPhase.Preparing, after.Phase);
        Assert.Equal(before.Revision + 1, after.Revision);
        // Only the registration's own event: a heartbeat appends none.
        Assert.Equal([RunEventType.AgentSupervisionRegistered], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task Heartbeats_never_change_the_attempt_the_run_or_the_budget_facts()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        var factsBefore = await scene.FactsAsync();
        var attemptBefore = await scene.AttemptFactsAsync(attemptId);

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.HeartbeatAsync(attemptId, observer);
        await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);
        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.HeartbeatAsync(attemptId, observer);

        // The phase entry appends its one event; the heartbeats append none, and nothing else changes.
        Assert.Equal(factsBefore with { Events = factsBefore.Events + 1 }, await scene.FactsAsync());
        Assert.Equal(
            [RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionPhaseEntered], await scene.SupervisionEventTypesAsync());
        Assert.Equal(attemptBefore, await scene.AttemptFactsAsync(attemptId));
    }

    [Fact]
    public async Task A_foreign_observer_cannot_heartbeat_advance_or_close_and_the_receipt_is_untouched()
    {
        var (scene, attemptId, owner) = await ObservedAsync();
        var before = await scene.ReceiptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        var stranger = Guid.NewGuid();

        var heartbeat = await scene.HeartbeatAsync(attemptId, stranger);
        var advance = await scene.AdvanceAsync(attemptId, stranger, AgentSupervisionPhase.AwaitingAdapterResult);
        var close = await scene.CloseAsync(attemptId, stranger);

        foreach (var code in new[] { heartbeat.Errors[0].Code, advance.Errors[0].Code, close.Errors[0].Code })
        {
            Assert.Equal(AgentSupervisionErrors.NotOwnerCode, code);
        }

        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal(owner, after.ObserverId);
        Assert.Equal((before.Revision, before.LastHeartbeatAtUtc, before.Phase, before.ClosedAtUtc),
            (after.Revision, after.LastHeartbeatAtUtc, after.Phase, after.ClosedAtUtc));
    }

    [Fact]
    public async Task Another_host_launch_cannot_write_an_observation_it_did_not_start()
    {
        var (scene, attemptId, observer) = await ObservedAsync();

        var heartbeat = await scene.HeartbeatAsync(attemptId, observer, hostInstanceId: Guid.NewGuid());
        var advance = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult, hostInstanceId: Guid.NewGuid());
        var close = await scene.CloseAsync(attemptId, observer, hostInstanceId: Guid.NewGuid());

        foreach (var result in new[] { heartbeat.Errors[0].Code, advance.Errors[0].Code, close.Errors[0].Code })
        {
            Assert.Equal(AgentSupervisionErrors.NotCurrentHostCode, result);
        }

        Assert.Equal(1, (await scene.ReceiptAsync(attemptId)).Revision);
    }

    [Fact]
    public async Task A_missing_observation_is_not_found()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();

        var result = await scene.HeartbeatAsync(attemptId, Guid.NewGuid());

        Assert.Equal(AgentSupervisionErrors.ObservationNotFoundCode, result.Errors[0].Code);
    }

    [Theory]
    [InlineData(AgentSupervisionPhase.AwaitingAdapterResult, 12)]
    [InlineData(AgentSupervisionPhase.RecordingResult, 2)]
    public async Task A_phase_entry_fixes_its_absolute_deadline_from_the_actual_reserved_timeout(AgentSupervisionPhase phase, int minutes)
    {
        var (scene, attemptId, observer) = await ObservedAsync(TimeSpan.FromMinutes(10));
        scene.Clock.Advance(TimeSpan.FromSeconds(7));

        var result = await scene.AdvanceAsync(attemptId, observer, phase);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        var receipt = await scene.ReceiptAsync(attemptId);
        var entered = AgentSupervisionTestScene.Start.AddSeconds(7);
        Assert.Equal(phase, receipt.Phase);
        Assert.Equal(entered, receipt.PhaseEnteredAtUtc);
        Assert.Equal(entered, receipt.LastHeartbeatAtUtc);
        Assert.Equal(entered.AddMinutes(minutes), receipt.PhaseDeadlineUtc);
        Assert.Equal(receipt.PhaseDeadlineUtc, result.Value.PhaseDeadlineUtc);
    }

    [Fact]
    public async Task The_adapter_phase_uses_each_attempts_own_reserved_timeout()
    {
        var (scene, attemptId, observer) = await ObservedAsync(TimeSpan.FromMinutes(3));

        await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);

        Assert.Equal(
            AgentSupervisionTestScene.Start.AddMinutes(5), (await scene.ReceiptAsync(attemptId)).PhaseDeadlineUtc);
    }

    [Fact]
    public async Task A_phase_never_repeats_regresses_or_extends_a_deadline_by_replay()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);
        var entered = await scene.ReceiptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromMinutes(1));

        var replay = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);
        var regress = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.Preparing);
        var undefined = await scene.AdvanceAsync(attemptId, observer, (AgentSupervisionPhase)99);

        Assert.Equal(AgentSupervisionErrors.PhaseNotForwardCode, replay.Errors[0].Code);
        Assert.Equal(AgentSupervisionErrors.PhaseNotForwardCode, regress.Errors[0].Code);
        Assert.Equal(AgentSupervisionErrors.InvalidRequestCode, undefined.Errors[0].Code);
        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal(
            (entered.Phase, entered.PhaseEnteredAtUtc, entered.PhaseDeadlineUtc), (after.Phase, after.PhaseEnteredAtUtc, after.PhaseDeadlineUtc));
        // The silent minute is a missed interval: the refused replay still records it once and a replay can never erase it.
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, after.AttentionReason);
    }

    [Fact]
    public async Task Phases_may_skip_forward_when_no_adapter_call_is_made()
    {
        var (scene, attemptId, observer) = await ObservedAsync();

        var result = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentSupervisionPhase.RecordingResult, (await scene.ReceiptAsync(attemptId)).Phase);
    }

    [Theory]
    [InlineData(29_999, false)]
    [InlineData(30_000, true)]
    public async Task A_late_heartbeat_first_records_the_stale_gap_it_would_otherwise_erase_at_the_exact_boundary(
        long milliseconds, bool attention)
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

        var result = await scene.HeartbeatAsync(attemptId, observer);

        Assert.True(result.IsSuccess);
        Assert.Equal(attention, result.Value.AttentionRecorded);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMilliseconds(milliseconds), receipt.LastHeartbeatAtUtc);
        if (attention)
        {
            Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
            Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), receipt.AttentionAtUtc);
            Assert.Equal(
                [RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention], await scene.SupervisionEventTypesAsync());
        }
        else
        {
            Assert.Null(receipt.AttentionReason);
            Assert.Equal([RunEventType.AgentSupervisionRegistered], await scene.SupervisionEventTypesAsync());
        }
    }

    [Fact]
    public async Task A_fresh_heartbeat_cannot_mask_a_hung_phase_whose_absolute_deadline_elapsed()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second < 120; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            var beat = await scene.HeartbeatAsync(attemptId, observer);
            Assert.False(beat.Value.AttentionRecorded);
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        var atDeadline = await scene.HeartbeatAsync(attemptId, observer);

        Assert.True(atDeadline.IsSuccess);
        Assert.True(atDeadline.Value.AttentionRecorded);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, receipt.AttentionReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), receipt.AttentionAtUtc);
        Assert.Equal(AgentSupervisionPhase.Preparing, receipt.Phase);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), receipt.PhaseDeadlineUtc);
    }

    [Fact]
    public async Task A_late_phase_writer_records_the_first_attention_before_the_phase_it_enters()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(45));

        var result = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AttentionRecorded);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionPhase.AwaitingAdapterResult, receipt.Phase);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), receipt.AttentionAtUtc);
    }

    [Fact]
    public async Task A_refused_phase_move_still_keeps_the_attention_it_found()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromMinutes(1));

        var refused = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.Preparing);

        Assert.Equal(AgentSupervisionErrors.PhaseNotForwardCode, refused.Errors[0].Code);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
        Assert.Equal(AgentSupervisionPhase.Preparing, receipt.Phase);
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task The_first_attention_is_retained_through_later_heartbeats_phases_and_deadlines()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(40));
        await scene.HeartbeatAsync(attemptId, observer);
        var first = await scene.ReceiptAsync(attemptId);

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.HeartbeatAsync(attemptId, observer);
        await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult);
        scene.Clock.Advance(TimeSpan.FromMinutes(3));
        var late = await scene.HeartbeatAsync(attemptId, observer);

        Assert.False(late.Value.AttentionRecorded);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(first.AttentionReason, receipt.AttentionReason);
        Assert.Equal(first.AttentionAtUtc, receipt.AttentionAtUtc);
        // One attention event only; the later phase entry appends its own event and the heartbeats none.
        Assert.Equal(
            [RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionPhaseEntered],
            await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task The_attention_event_carries_only_identities_the_phase_and_the_fixed_reason()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(31));
        await scene.HeartbeatAsync(attemptId, observer);

        var events = await scene.SupervisionEventsAsync();
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention], events.Select(candidate => candidate.EventType));
        var runEvent = events[1];

        Assert.Equal(attemptId, runEvent.AttemptId);
        using var payload = JsonDocument.Parse(runEvent.PayloadJson);
        Assert.Equal(
            ["attemptId", "phase", "reason", "supervisionId"],
            payload.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("stale_heartbeat", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Preparing", payload.RootElement.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task A_backward_clock_is_refused_and_writes_nothing()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.HeartbeatAsync(attemptId, observer);
        var before = await scene.ReceiptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(-4));

        var heartbeat = await scene.HeartbeatAsync(attemptId, observer);
        var advance = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult);

        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, heartbeat.Errors[0].Code);
        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, advance.Errors[0].Code);
        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal((before.Revision, before.LastHeartbeatAtUtc, before.Phase), (after.Revision, after.LastHeartbeatAtUtc, after.Phase));
    }

    [Fact]
    public async Task A_closed_observation_refuses_every_late_writer()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        await scene.CloseAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(5));

        var heartbeat = await scene.HeartbeatAsync(attemptId, observer);
        var advance = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult);
        var close = await scene.CloseAsync(attemptId, observer);

        foreach (var code in new[] { heartbeat.Errors[0].Code, advance.Errors[0].Code, close.Errors[0].Code })
        {
            Assert.Equal(AgentSupervisionErrors.ClosedCode, code);
        }
    }

    [Fact]
    public async Task Racing_heartbeats_from_independent_connections_each_commit_or_refuse_without_corrupting_the_revision()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(5));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => scene.HeartbeatAsync(attemptId, observer))));

        var committed = results.Count(result => result.IsSuccess);
        Assert.True(committed >= 1);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(1 + committed, receipt.Revision);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(5), receipt.LastHeartbeatAtUtc);
    }
}
