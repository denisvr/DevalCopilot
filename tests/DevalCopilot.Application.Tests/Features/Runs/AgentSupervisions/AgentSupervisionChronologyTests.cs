using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 and ASUP-R3: a recorded closure, completion or first attention that lies after the current host clock (for example
/// after a clock rollback) is an impossible fact. The read is <c>Unavailable</c>, never <c>Ended</c> or <c>NeedsAttention</c>, and a
/// closure is refused rather than clamped. A timely result read long afterwards, an exact boundary, the precedence of an actual terminal
/// attempt and the retained attention history stay as they were. Real file-backed SQLite, production handlers, controllable clock.</summary>
public sealed class AgentSupervisionChronologyTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task A_terminal_observation_with_a_future_closure_is_unavailable_after_clock_rollback()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attempt, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True((await scene.CloseAsync(attempt, observer)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromSeconds(-5));

        var reading = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, reading.Status);
        Assert.Equal(AgentSupervisionVocabulary.ChronologyInvalid, reading.ReasonCode);
        Assert.Null(reading.Attention);
    }

    [Fact]
    public async Task A_terminal_completion_in_the_future_is_unavailable_after_clock_rollback()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attempt, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromSeconds(-5));

        var reading = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, reading.Status);
        Assert.Equal(AgentSupervisionVocabulary.ChronologyInvalid, reading.ReasonCode);
    }

    [Fact]
    public async Task A_recorded_attention_in_the_future_is_unavailable_after_clock_rollback()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await scene.SweepAsync((await scene.ReceiptAsync(attempt)).Id)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromSeconds(-11));

        var reading = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, reading.Status);
        Assert.Equal(AgentSupervisionVocabulary.ChronologyInvalid, reading.ReasonCode);
        Assert.Null(reading.Attention);
    }

    [Fact]
    public async Task Closing_from_a_completion_that_lies_after_the_clock_is_refused_and_leaves_the_observation_open()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attempt, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromSeconds(-5));
        var eventsBefore = await scene.SupervisionEventsAsync();

        var close = await scene.CloseAsync(attempt, observer);
        var sweep = await scene.SweepAsync((await scene.ReceiptAsync(attempt)).Id);

        Assert.True(close.IsFailure);
        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, close.Errors[0].Code);
        Assert.True(sweep.IsFailure);
        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, sweep.Errors[0].Code);
        var receipt = await scene.ReceiptAsync(attempt);
        Assert.True(receipt.IsOpen);
        Assert.Null(receipt.AttentionReason);
        Assert.Equal(
            eventsBefore.Select(runEvent => runEvent.Id),
            (await scene.SupervisionEventsAsync()).Select(runEvent => runEvent.Id));
    }

    [Fact]
    public async Task A_timely_result_read_long_afterwards_is_still_ended_without_attention()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attempt, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await scene.CloseAsync(attempt, observer)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromHours(3));

        var reading = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Ended, reading.Status);
        Assert.Null(reading.Attention);
    }

    [Fact]
    public async Task A_completion_and_a_closure_read_at_their_exact_instant_are_valid()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attempt, AttemptStatus.Completed);

        var atCompletion = await scene.ReadValueAsync();
        Assert.True((await scene.CloseAsync(attempt, observer)).IsSuccess);
        var atClosure = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Ended, atCompletion.Status);
        Assert.Equal(AgentSupervisionStatus.Ended, atClosure.Status);
        Assert.Equal(scene.Clock.GetUtcNow(), atClosure.Observation!.ClosedAtUtc);
    }

    [Fact]
    public async Task An_actual_terminal_attempt_still_wins_and_keeps_its_earlier_recorded_attention_as_history()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attempt = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attempt);
        scene.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await scene.SweepAsync((await scene.ReceiptAsync(attempt)).Id)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromSeconds(9));
        await scene.CompleteAsync(attempt, AttemptStatus.Failed);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await scene.CloseAsync(attempt, observer)).IsSuccess);
        scene.Clock.Advance(TimeSpan.FromMinutes(10));

        var reading = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Ended, reading.Status);
        Assert.Equal(AttemptStatus.Failed, reading.Attempt!.Status);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, reading.Attention!.RecordedReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), reading.Attention.RecordedAtUtc);
    }
}
