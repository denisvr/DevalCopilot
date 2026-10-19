using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 and ASUP-R4: every material observation transition (registration, each phase entry and every actual closure,
/// including a clean one after a terminal result) appends exactly one bounded, fixed-schema run event in the same transaction as the
/// receipt change; a heartbeat appends none, a replay or refused move appends none, and a rejected event rolls the receipt change back.
/// The durable event-type strings are written literally here on purpose: they are schema.</summary>
public sealed class AgentSupervisionEventTests : IAsyncLifetime
{
    private const string Registered = "agent_supervision.registered";
    private const string PhaseEntered = "agent_supervision.phase_entered";
    private const string Attention = "agent_supervision.attention";
    private const string Closed = "agent_supervision.closed";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static Dictionary<string, JsonElement> PayloadOf(RunEvent runEvent) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(runEvent.PayloadJson)!;

    private async Task BlockEventInsertAsync(string eventType)
    {
        var sql = eventType switch
        {
            Registered => "CREATE TRIGGER trg_test_block_supervision_event BEFORE INSERT ON events "
                + "WHEN NEW.\"EventType\" = 'agent_supervision.registered' BEGIN SELECT RAISE(ABORT, 'blocked'); END;",
            PhaseEntered => "CREATE TRIGGER trg_test_block_supervision_event BEFORE INSERT ON events "
                + "WHEN NEW.\"EventType\" = 'agent_supervision.phase_entered' BEGIN SELECT RAISE(ABORT, 'blocked'); END;",
            Closed => "CREATE TRIGGER trg_test_block_supervision_event BEFORE INSERT ON events "
                + "WHEN NEW.\"EventType\" = 'agent_supervision.closed' BEGIN SELECT RAISE(ABORT, 'blocked'); END;",
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task Registration_appends_exactly_one_fixed_schema_event_and_a_replay_appends_none()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        var replay = await scene.RegisterAsync(attemptId, observer);

        Assert.True(replay.Value.Replayed);
        var runEvent = Assert.Single(await scene.SupervisionEventsAsync());
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(Registered, runEvent.EventType);
        Assert.Equal(attemptId, runEvent.AttemptId);
        Assert.Equal(receipt.StartedAtUtc, runEvent.OccurredAtUtc);
        var payload = PayloadOf(runEvent);
        Assert.Equal(["attemptId", "phase", "supervisionId"], payload.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(receipt.Id.ToString(), payload["supervisionId"].GetString());
        Assert.Equal(nameof(AgentSupervisionPhase.Preparing), payload["phase"].GetString());
    }

    [Fact]
    public async Task Each_phase_entry_appends_exactly_one_event_with_its_phase_and_a_refused_move_or_heartbeat_appends_none()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await scene.HeartbeatAsync(attemptId, observer)).IsSuccess);
        var replayed = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);
        scene.Clock.Advance(TimeSpan.FromSeconds(2));
        var recording = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.RecordingResult);

        Assert.Equal(AgentSupervisionErrors.PhaseNotForwardCode, replayed.Errors[0].Code);
        Assert.True(recording.IsSuccess);
        Assert.False(recording.Value.AttentionRecorded);
        var events = await scene.SupervisionEventsAsync();
        Assert.Equal([Registered, PhaseEntered, PhaseEntered], events.Select(runEvent => runEvent.EventType));
        Assert.Equal(
            [nameof(AgentSupervisionPhase.AwaitingAdapterResult), nameof(AgentSupervisionPhase.RecordingResult)],
            events.Skip(1).Select(runEvent => PayloadOf(runEvent)["phase"].GetString()));
        Assert.All(events, runEvent => Assert.Equal(["attemptId", "phase", "supervisionId"], PayloadOf(runEvent).Keys.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_phase_entry_after_a_missed_deadline_records_the_attention_before_the_phase_event()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(31));

        var result = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);

        Assert.True(result.Value.AttentionRecorded);
        Assert.Equal([Registered, Attention, PhaseEntered], (await scene.SupervisionEventsAsync()).Select(runEvent => runEvent.EventType));
    }

    [Theory]
    [InlineData(AttemptStatus.Completed)]
    [InlineData(AttemptStatus.Failed)]
    [InlineData(AttemptStatus.Interrupted)]
    public async Task A_clean_closure_after_a_terminal_result_appends_exactly_one_closed_event_and_a_second_close_none(AttemptStatus status)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(3));
        await scene.CompleteAsync(attemptId, status);

        Assert.True((await scene.CloseAsync(attemptId, observer)).IsSuccess);
        var again = await scene.CloseAsync(attemptId, observer);
        var sweep = await scene.SweepAsync((await scene.ReceiptAsync(attemptId)).Id);

        Assert.Equal(AgentSupervisionErrors.ClosedCode, again.Errors[0].Code);
        Assert.True(sweep.IsSuccess);
        var events = await scene.SupervisionEventsAsync();
        Assert.Equal([Registered, Closed], events.Select(runEvent => runEvent.EventType));
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, PayloadOf(events[1])["reason"].GetString());
    }

    [Fact]
    public async Task A_sweep_that_closes_a_terminal_attempt_cleanly_appends_exactly_one_closed_event()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        await scene.ObserveAttemptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(3));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        var supervisionId = (await scene.ReceiptAsync(attemptId)).Id;

        Assert.True((await scene.SweepAsync(supervisionId)).Value.Closed);
        Assert.True((await scene.SweepAsync(supervisionId)).IsSuccess);

        Assert.Equal([Registered, Closed], (await scene.SupervisionEventsAsync()).Select(runEvent => runEvent.EventType));
    }

    [Fact]
    public async Task A_rejected_registration_event_rolls_the_receipt_back()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        await BlockEventInsertAsync(Registered);

        var result = await scene.RegisterAsync(attemptId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.PersistenceFailedCode, result.Errors[0].Code);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.AgentSupervisions.ToListAsync());
        Assert.Empty(await scene.SupervisionEventsAsync());
    }

    [Fact]
    public async Task A_rejected_phase_event_rolls_the_phase_back()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        await BlockEventInsertAsync(PhaseEntered);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));

        var result = await scene.AdvanceAsync(attemptId, observer, AgentSupervisionPhase.AwaitingAdapterResult);

        Assert.Equal(AgentSupervisionErrors.PersistenceFailedCode, result.Errors[0].Code);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal((AgentSupervisionPhase.Preparing, 1L), (receipt.Phase, receipt.Revision));
        Assert.Equal([Registered], (await scene.SupervisionEventsAsync()).Select(runEvent => runEvent.EventType));
    }

    [Fact]
    public async Task A_rejected_clean_closure_event_leaves_the_observation_open()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        await BlockEventInsertAsync(Closed);

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionErrors.PersistenceFailedCode, result.Errors[0].Code);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.True(receipt.IsOpen);
        Assert.Equal(1, receipt.Revision);
        Assert.Equal([Registered], (await scene.SupervisionEventsAsync()).Select(runEvent => runEvent.EventType));
    }
}
