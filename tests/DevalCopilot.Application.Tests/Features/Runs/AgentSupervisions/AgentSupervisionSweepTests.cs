using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 independent observation: the sweep records the first attention without any cooperation of a blocked supervisor,
/// is idempotent, preserves a timely terminal result, closes an old host's observation as interrupted without adopting it, and its
/// bounded window visits every open observation without starvation. Startup closure never adopts, renews or resumes.</summary>
public sealed class AgentSupervisionSweepTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(AgentSupervisionTestScene Scene, Guid AttemptId, Guid Observer, Guid SupervisionId)> ObservedAsync()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        var observer = await scene.ObserveAttemptAsync(attemptId);
        return (scene, attemptId, observer, (await scene.ReceiptAsync(attemptId)).Id);
    }

    [Fact]
    public async Task A_silent_observer_is_recorded_once_by_the_sweep_with_no_supervisor_cooperation()
    {
        var (scene, attemptId, _, supervisionId) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(31));

        var first = await scene.SweepAsync(supervisionId);
        var second = await scene.SweepAsync(supervisionId);

        Assert.True(first.Value.Changed);
        Assert.False(first.Value.Closed);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, first.Value.AttentionReason);
        Assert.False(second.Value.Changed);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), receipt.AttentionAtUtc);
        Assert.Null(receipt.ClosedAtUtc);
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention], await scene.SupervisionEventTypesAsync());
        Assert.Equal(AttemptStatus.Running, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task A_healthy_heartbeat_does_not_hide_a_phase_deadline_from_the_sweep()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        for (var second = 5; second < 125; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            if (second < 120)
            {
                await scene.HeartbeatAsync(attemptId, observer);
            }
        }

        // The last heartbeat was five seconds ago; the phase deadline was reached at the 120 second mark.
        var swept = await scene.SweepAsync(supervisionId);

        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, swept.Value.AttentionReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), (await scene.ReceiptAsync(attemptId)).AttentionAtUtc);
    }

    [Fact]
    public async Task Nothing_is_recorded_while_the_observation_is_fresh_and_within_its_deadline()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(29));

        var swept = await scene.SweepAsync(supervisionId);

        Assert.False(swept.Value.Changed);
        Assert.Null(swept.Value.AttentionReason);
        Assert.Equal(1, (await scene.ReceiptAsync(attemptId)).Revision);
        Assert.Equal([RunEventType.AgentSupervisionRegistered], await scene.SupervisionEventTypesAsync());
        Assert.NotEqual(Guid.Empty, observer);
    }

    [Fact]
    public async Task A_timely_terminal_result_is_closed_by_the_sweep_without_being_turned_into_a_late_failure()
    {
        var (scene, attemptId, _, supervisionId) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromMinutes(20));

        var swept = await scene.SweepAsync(supervisionId);

        Assert.True(swept.Value.Closed);
        Assert.Null(swept.Value.AttentionReason);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, receipt.ClosureReason);
        Assert.Equal(AttemptStatus.Completed, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task A_terminal_result_at_the_exact_deadline_is_late_and_its_attention_is_kept_as_history()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        for (var second = 5; second < 120; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromMinutes(1));

        var swept = await scene.SweepAsync(supervisionId);

        Assert.True(swept.Value.Closed);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, swept.Value.AttentionReason);
        Assert.Equal(AttemptStatus.Completed, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task An_old_hosts_observation_is_closed_as_interrupted_and_never_adopted()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        var newHost = new AgentSupervisionHostInstance(Guid.NewGuid());
        scene.Clock.Advance(TimeSpan.FromSeconds(1));

        var swept = await scene.SweepAsync(supervisionId, newHost);

        Assert.True(swept.Value.Closed);
        Assert.Equal(AgentSupervisionAttentionReason.HostInterrupted, swept.Value.AttentionReason);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionClosureReason.HostInterrupted, receipt.ClosureReason);
        Assert.Equal(scene.Host.Id, receipt.HostInstanceId);
        Assert.Equal(observer, receipt.ObserverId);
        Assert.Equal(AttemptStatus.Running, (await scene.AttemptFactsAsync(attemptId)).Status);
    }

    [Fact]
    public async Task A_missing_observation_is_not_found_and_a_closed_one_is_left_unchanged()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        var missing = await scene.SweepAsync(Guid.NewGuid());
        await scene.CloseAsync(attemptId, observer);
        var before = await scene.ReceiptAsync(attemptId);

        var closed = await scene.SweepAsync(supervisionId);

        Assert.Equal(AgentSupervisionErrors.ObservationNotFoundCode, missing.Errors[0].Code);
        Assert.False(closed.Value.Changed);
        Assert.True(closed.Value.Closed);
        Assert.Equal(before.Revision, (await scene.ReceiptAsync(attemptId)).Revision);
    }

    [Fact]
    public async Task A_sweep_racing_the_observers_own_heartbeat_never_loses_the_first_attention()
    {
        var (scene, attemptId, observer, supervisionId) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(31));

        var firstSweep = Task.Run(() => scene.SweepAsync(supervisionId));
        var heartbeat = Task.Run(() => scene.HeartbeatAsync(attemptId, observer));
        var secondSweep = Task.Run(() => scene.SweepAsync(supervisionId));
        await Task.WhenAll(firstSweep, heartbeat, secondSweep);

        Assert.True((await firstSweep).IsSuccess);
        Assert.True((await heartbeat).IsSuccess);
        Assert.True((await secondSweep).IsSuccess);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), receipt.AttentionAtUtc);
        Assert.Equal([RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task The_bounded_window_visits_every_open_observation_across_rounds_without_starvation()
    {
        // The database allows one Running attempt per run, so every open observation belongs to its own run.
        var total = AgentSupervisionPolicy.SweepBatchSize + 7;
        AgentSupervisionTestScene? last = null;
        for (var index = 0; index < total; index++)
        {
            last = await AgentSupervisionTestScene.CreateAsync(fixture);
            await last.ObserveAttemptAsync(await last.ClaimAsync());
        }

        var visited = new HashSet<Guid>();
        Guid? cursor = null;
        var rounds = 0;
        while (visited.Count < total && rounds++ < 5)
        {
            var window = await last!.OpenWindowAsync(cursor);
            Assert.True(window.SupervisionIds.Count <= AgentSupervisionPolicy.SweepBatchSize);
            Assert.Equal(window.SupervisionIds.Count, window.SupervisionIds.Distinct().Count());
            visited.UnionWith(window.SupervisionIds);
            cursor = window.NextCursor;
        }

        Assert.Equal(total, visited.Count);
        Assert.True(rounds <= 3);
    }

    [Fact]
    public async Task The_window_is_empty_without_open_observations_and_ignores_closed_ones()
    {
        var (scene, attemptId, observer, _) = await ObservedAsync();
        await scene.CloseAsync(attemptId, observer);

        var window = await scene.OpenWindowAsync(null);

        Assert.Empty(window.SupervisionIds);
        Assert.Null(window.NextCursor);
    }

    [Fact]
    public async Task Startup_closure_closes_every_previous_host_observation_as_interrupted_and_changes_no_attempt()
    {
        // Two runs: one whose attempt restart reconciliation already interrupted, one still Running.
        var reconciledScene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var runningScene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var reconciled = await reconciledScene.ClaimAsync(AgentResponseContract.CriticalReview);
        var running = await runningScene.ClaimAsync();
        await reconciledScene.ObserveAttemptAsync(reconciled);
        await runningScene.ObserveAttemptAsync(running);
        reconciledScene.Clock.Advance(TimeSpan.FromSeconds(8));
        await reconciledScene.CompleteAsync(reconciled, AttemptStatus.Interrupted);
        var attemptsBefore = (await runningScene.AttemptFactsAsync(running), await reconciledScene.AttemptFactsAsync(reconciled));
        runningScene.Clock.Advance(TimeSpan.FromMinutes(5));

        var result = await runningScene.CloseInterruptedAsync(new AgentSupervisionHostInstance(Guid.NewGuid()));

        Assert.Equal(2, result.Value.ClosedCount);
        Assert.Equal(0, result.Value.UnclosedCount);
        foreach (var (scene, attemptId) in new[] { (runningScene, running), (reconciledScene, reconciled) })
        {
            var receipt = await scene.ReceiptAsync(attemptId);
            Assert.Equal(AgentSupervisionClosureReason.HostInterrupted, receipt.ClosureReason);
            Assert.Equal(AgentSupervisionAttentionReason.HostInterrupted, receipt.AttentionReason);
            Assert.Equal(scene.Host.Id, receipt.HostInstanceId);
            Assert.Equal(
                [RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionClosed],
                await scene.SupervisionEventTypesAsync());
        }

        Assert.Equal(attemptsBefore, (await runningScene.AttemptFactsAsync(running), await reconciledScene.AttemptFactsAsync(reconciled)));
        Assert.Empty((await runningScene.OpenWindowAsync(null)).SupervisionIds);
    }

    [Fact]
    public async Task Startup_closure_keeps_an_attention_the_previous_host_had_already_recorded()
    {
        var (scene, attemptId, _, supervisionId) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(31));
        await scene.SweepAsync(supervisionId);
        scene.Clock.Advance(TimeSpan.FromMinutes(1));

        await scene.CloseInterruptedAsync(new AgentSupervisionHostInstance(Guid.NewGuid()));

        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, receipt.AttentionReason);
        Assert.Equal(AgentSupervisionClosureReason.HostInterrupted, receipt.ClosureReason);
    }

    [Fact]
    public async Task Startup_closure_leaves_the_current_hosts_open_observation_and_a_second_run_changes_nothing()
    {
        var (scene, attemptId, _, _) = await ObservedAsync();

        var same = await scene.CloseInterruptedAsync();
        var other = await scene.CloseInterruptedAsync(new AgentSupervisionHostInstance(Guid.NewGuid()));
        var again = await scene.CloseInterruptedAsync(new AgentSupervisionHostInstance(Guid.NewGuid()));

        Assert.Equal(0, same.Value.ClosedCount);
        Assert.Equal(1, other.Value.ClosedCount);
        Assert.Equal(0, again.Value.ClosedCount);
        Assert.NotNull((await scene.ReceiptAsync(attemptId)).ClosedAtUtc);
    }

    [Fact]
    public async Task Startup_closure_never_closes_an_observation_whose_times_do_not_cohere_with_the_clock()
    {
        var (scene, attemptId, observer, _) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(30));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(-20));

        var result = await scene.CloseInterruptedAsync(new AgentSupervisionHostInstance(Guid.NewGuid()));

        Assert.Equal(0, result.Value.ClosedCount);
        Assert.Equal(1, result.Value.UnclosedCount);
        Assert.Null((await scene.ReceiptAsync(attemptId)).ClosedAtUtc);
    }

    [Fact]
    public async Task The_sweep_never_writes_the_attempt_the_run_or_the_budget_facts()
    {
        var (scene, attemptId, _, supervisionId) = await ObservedAsync();
        var factsBefore = await scene.FactsAsync();
        var attemptBefore = await scene.AttemptFactsAsync(attemptId);
        scene.Clock.Advance(TimeSpan.FromMinutes(10));

        await scene.SweepAsync(supervisionId);

        var factsAfter = await scene.FactsAsync();
        Assert.Equal((factsBefore.Attempts, factsBefore.Runs, factsBefore.Lifecycle), (factsAfter.Attempts, factsAfter.Runs, factsAfter.Lifecycle));
        Assert.Equal(attemptBefore, await scene.AttemptFactsAsync(attemptId));
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Attempts.CountAsync(candidate => candidate.Id == attemptId && candidate.Status == AttemptStatus.Running));
    }
}
