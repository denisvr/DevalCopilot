using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 closure from a fresh durable read of the Attempt, never from a method return: a terminal attempt closes cleanly
/// (timely or late judged at its own completion), a still Running attempt records that the observer ended, and the first attention
/// survives. Closure never terminalizes, retries or cancels anything.</summary>
public sealed class AgentSupervisionClosureTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(AgentSupervisionTestScene Scene, Guid AttemptId, Guid Observer)> ObservedAsync()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        return (scene, attemptId, await scene.ObserveAttemptAsync(attemptId));
    }

    [Theory]
    [InlineData(AttemptStatus.Completed)]
    [InlineData(AttemptStatus.Failed)]
    [InlineData(AttemptStatus.Interrupted)]
    public async Task A_timely_terminal_attempt_closes_cleanly_with_no_attention_and_only_its_closed_event(AttemptStatus status)
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.CompleteAsync(attemptId, status);

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, result.Value.ClosureReason);
        Assert.Null(result.Value.AttentionReason);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.NotNull(receipt.ClosedAtUtc);
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionClosed], await scene.SupervisionEventTypesAsync());
        Assert.Equal(status, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task A_still_running_attempt_records_that_the_observer_ended_and_never_terminalizes_it()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        var attemptBefore = await scene.AttemptFactsAsync(attemptId);
        var factsBefore = await scene.FactsAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(3));

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, result.Value.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, result.Value.AttentionReason);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(3), receipt.AttentionAtUtc);
        Assert.Equal(
            [RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionClosed],
            await scene.SupervisionEventTypesAsync());
        Assert.Equal(attemptBefore, await scene.AttemptFactsAsync(attemptId));
        var factsAfter = await scene.FactsAsync();
        Assert.Equal((factsBefore.Attempts, factsBefore.Runs, factsBefore.Lifecycle), (factsAfter.Attempts, factsAfter.Runs, factsAfter.Lifecycle));
    }

    [Fact]
    public async Task Closure_keeps_an_earlier_first_attention_instead_of_replacing_it()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(40));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, result.Value.AttentionReason);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, (await scene.ReceiptAsync(attemptId)).AttentionReason);
    }

    [Fact]
    public async Task Closing_a_hung_phase_that_nobody_swept_records_the_deadline_first()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second <= 125; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, result.Value.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, result.Value.AttentionReason);
    }

    [Fact]
    public async Task A_late_durable_result_keeps_its_attempt_valid_and_the_closure_records_the_missed_deadline_as_history()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second < 120; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, result.Value.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, result.Value.AttentionReason);
        Assert.Equal(AttemptStatus.Completed, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task A_timely_result_that_is_only_closed_long_afterwards_is_not_turned_into_a_late_failure()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.HeartbeatAsync(attemptId, observer);
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, result.Value.ClosureReason);
        Assert.Null(result.Value.AttentionReason);
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionClosed], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task Closure_reads_the_attempt_afresh_so_a_result_that_committed_after_an_earlier_read_is_seen()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        var first = await scene.ReadValueAsync();
        Assert.Equal(AttemptStatus.Running, first.Attempt!.Status);
        await scene.CompleteAsync(attemptId, AttemptStatus.Failed);

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, result.Value.ClosureReason);
    }

    [Fact]
    public async Task A_closed_observation_is_final_and_a_second_close_is_refused()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        await scene.CloseAsync(attemptId, observer);
        var before = await scene.ReceiptAsync(attemptId);

        var again = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionErrors.ClosedCode, again.Errors[0].Code);
        var after = await scene.ReceiptAsync(attemptId);
        Assert.Equal((before.Revision, before.ClosedAtUtc, before.ClosureReason), (after.Revision, after.ClosedAtUtc, after.ClosureReason));
    }

    [Fact]
    public async Task A_closure_that_cannot_be_proved_coherent_with_the_clock_leaves_the_observation_open()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(-5));

        var result = await scene.CloseAsync(attemptId, observer);

        Assert.Equal(AgentSupervisionErrors.ChronologyInvalidCode, result.Errors[0].Code);
        Assert.Null((await scene.ReceiptAsync(attemptId)).ClosedAtUtc);
    }

    [Fact]
    public async Task Closure_and_its_events_commit_atomically_so_a_rejected_write_leaves_the_observation_open()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        // A trigger that refuses the event insert makes the closure's events unwritable: the receipt change is in the same transaction
        // and must roll back with them, never leaving a closed observation whose events are missing.
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER trg_test_block_events BEFORE INSERT ON events BEGIN SELECT RAISE(ABORT, 'blocked'); END;");
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        var failure = await scene.CloseAsync(attemptId, observer);

        Assert.True(failure.IsFailure);
        Assert.Equal(AgentSupervisionErrors.PersistenceFailedCode, failure.Errors[0].Code);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Null(receipt.ClosedAtUtc);
        Assert.Null(receipt.AttentionReason);
        Assert.Equal(1, receipt.Revision);
    }
}
