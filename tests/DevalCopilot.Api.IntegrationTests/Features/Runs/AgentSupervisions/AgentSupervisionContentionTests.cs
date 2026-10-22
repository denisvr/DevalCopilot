using System.Diagnostics;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// ADR-0056 and ASUP-R1: every observation call and the sweep are bounded to two seconds even when the store blocks synchronously. A
/// separate connection holds a real SQLite write transaction on the same file-backed database, so the observation's own transaction
/// start blocks inside the provider before any awaitable work exists (a noncooperative seam that ignores the call's token). The bound must
/// still hold for registration, phase entry, closure and the sweep; late writes must stay ordered and fenced; and an observation
/// timeout must never become an Agent outcome. Production mediator, production observer and sweep, no provider.
/// </summary>
public sealed class AgentSupervisionContentionTests
{
    // The two-second contract plus scheduling tolerance; the held transaction lasts well beyond it.
    private static readonly TimeSpan Tolerated = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Held = TimeSpan.FromSeconds(6);

    // A separate finite bound for teardown completion proof, never part of a measured stop.
    private static readonly TimeSpan TeardownBound = TimeSpan.FromSeconds(20);

    // The durable event-type strings, written literally on purpose: they are schema.
    private const string Registered = "agent_supervision.registered";
    private const string PhaseEntered = "agent_supervision.phase_entered";
    private const string Attention = "agent_supervision.attention";
    private const string Closed = "agent_supervision.closed";

    private static async Task<(Guid RunId, Guid AttemptId)> SeedRunningAttemptAsync(PlanningSequenceHostedHarness harness)
    {
        var scene = await harness.SeedSceneAsync();
        await using var seed = harness.OpenContext();
        var now = DateTimeOffset.UtcNow;
        var run = await seed.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        run.Claim(now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, scene.Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, now, 1);
        seed.Attempts.Add(attempt);
        await seed.SaveChangesAsync();
        return (scene.RunId, attempt.Id);
    }

    /// <summary>Takes the database write lock on its own connection and releases it after <paramref name="duration"/>.</summary>
    private static async Task<Task> HoldWriteLockAsync(PlanningSequenceHostedHarness harness, TimeSpan duration)
    {
        var locker = harness.OpenContext();
        var held = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlRawAsync("UPDATE runs SET \"Lifecycle\" = \"Lifecycle\" WHERE 0 = 1;");
        return Task.Run(async () =>
        {
            await Task.Delay(duration);
            await held.RollbackAsync();
            await held.DisposeAsync();
            await locker.DisposeAsync();
        });
    }

    private static async Task AssertAttemptUntouchedAsync(PlanningSequenceHostedHarness harness, Guid attemptId)
    {
        await using var context = harness.OpenContext();
        var attempt = await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
    }

    [Fact]
    public async Task Registration_returns_within_its_bound_while_a_real_writer_holds_the_database()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        await using var harness = new PlanningSequenceHostedHarness((services, _) => AgentSupervisionHostedSupport.Register(services, null, host));
        var (runId, attemptId) = await SeedRunningAttemptAsync(harness);
        var observer = harness.Provider.GetRequiredService<AgentSupervisionObserver>();

        var release = await HoldWriteLockAsync(harness, Held);
        var watch = Stopwatch.StartNew();
        var observation = await observer.BeginAsync(runId, attemptId);
        var elapsed = watch.Elapsed;
        await observation.DisposeAsync();
        await release;
        await SettleAbandonedWritesAsync();

        Assert.True(elapsed < Tolerated, $"Registration took {elapsed.TotalMilliseconds:F0} ms against a two-second bound.");
        Assert.False(observation.IsActive);

        // The abandoned registration outlived its own token while the writer held the database, so it was fenced and wrote nothing;
        // a later observer starts cleanly, and the timeout never became an Agent outcome.
        Assert.Null(await AgentSupervisionHostedSupport.ReceiptAsync(harness.Provider, attemptId));
        Assert.Empty(await AgentSupervisionHostedSupport.SupervisionEventsAsync(harness.Provider, runId));
        await AssertAttemptUntouchedAsync(harness, attemptId);
    }

    /// <summary>A write abandoned by its bound settles on its own once the writer released the database (its token already expired);
    /// the harness deletes the database file, so the test lets that last connection close first.</summary>
    private static Task SettleAbandonedWritesAsync() => Task.Delay(AgentSupervisionPolicy.CallBound);

    [Fact]
    public async Task Phase_entry_and_closure_return_within_their_bound_and_their_late_writes_stay_ordered_and_fenced()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        await using var harness = new PlanningSequenceHostedHarness((services, _) => AgentSupervisionHostedSupport.Register(services, null, host));
        var (runId, attemptId) = await SeedRunningAttemptAsync(harness);
        var observation = await harness.Provider.GetRequiredService<AgentSupervisionObserver>().BeginAsync(runId, attemptId);
        Assert.True(observation.IsActive);

        var release = await HoldWriteLockAsync(harness, Held);
        var phaseWatch = Stopwatch.StartNew();
        await observation.EnterAwaitingAdapterResultAsync();
        var phaseElapsed = phaseWatch.Elapsed;
        var disposeWatch = Stopwatch.StartNew();
        await observation.DisposeAsync();
        var disposeElapsed = disposeWatch.Elapsed;
        await release;

        Assert.True(phaseElapsed < Tolerated, $"Phase entry took {phaseElapsed.TotalMilliseconds:F0} ms against a two-second bound.");
        Assert.True(disposeElapsed < Tolerated, $"Closure took {disposeElapsed.TotalMilliseconds:F0} ms against a two-second bound.");

        // The closure waited behind the pending phase write and committed once the writer released the database: never before it,
        // never twice, and never on the attempt itself.
        var closed = await AgentSupervisionHostedSupport.WaitAsync(async () =>
        {
            var receipt = await AgentSupervisionHostedSupport.ReceiptAsync(harness.Provider, attemptId);
            return receipt is { ClosedAtUtc: not null } ? receipt : null;
        });
        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, closed.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, closed.AttentionReason);
        var types = (await AgentSupervisionHostedSupport.SupervisionEventsAsync(harness.Provider, runId)).Select(runEvent => runEvent.EventType).ToList();
        Assert.Equal(Registered, types[0]);
        Assert.Equal(Closed, types[^1]);
        Assert.Single(types, type => type == Registered);
        Assert.Single(types, type => type == Closed);
        Assert.True(types.Count(type => type == PhaseEntered) <= 1);
        await AssertAttemptUntouchedAsync(harness, attemptId);
        await SettleAbandonedWritesAsync();
    }

    [Fact]
    public async Task A_stopped_sweeps_pending_call_outlives_the_bounded_stop_and_its_scope_settles_only_once_the_writer_releases()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        var clock = new OffsetClock();
        await using var harness = new PlanningSequenceHostedHarness((services, _) => AgentSupervisionHostedSupport.Register(services, clock, host));
        var (runId, attemptId) = await SeedRunningAttemptAsync(harness);
        Assert.True((await harness.SendAsync(new RegisterAgentSupervisionCommand(runId, attemptId, host.Id, Guid.NewGuid()))).IsSuccess);
        clock.Advance(TimeSpan.FromSeconds(31));

        var release = await HoldWriteLockAsync(harness, TimeSpan.FromSeconds(5));
        var scopes = new TrackingScopeFactory(harness.Provider.GetRequiredService<IServiceScopeFactory>());
        var sweep = AgentSupervisionHostedSupport.CreateSweep(harness.Provider, scopes);
        await sweep.StartAsync(CancellationToken.None);
        await AgentSupervisionHostedSupport.WaitAsync(() => Task.FromResult(scopes.Active > 0 ? scopes : null));

        var stopWatch = Stopwatch.StartNew();
        await AgentSupervisionHostedSupport.StopAsync(sweep);

        // The stop is bounded, yet the call it left behind still owns its scope (and its database connection): deleting the database now
        // is the lifetime gap this regression pins, and only the settled pending Tasks (their scopes disposed inside them) prove the call completed.
        Assert.True(stopWatch.Elapsed < Tolerated, $"The sweep took {stopWatch.Elapsed.TotalMilliseconds:F0} ms to stop.");
        Assert.True(scopes.Active > 0, "A call that outlived the bounded stop must still be alive.");
        await release;
        await SweepCompletion.WaitForSettledAsync(sweep, TeardownBound);
        Assert.Equal(0, scopes.Active);
    }

    [Fact]
    public async Task The_sweep_stops_within_its_bound_while_a_real_writer_holds_the_database_and_later_records_each_attention_once()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        var clock = new OffsetClock();
        await using var harness = new PlanningSequenceHostedHarness((services, _) => AgentSupervisionHostedSupport.Register(services, clock, host));
        var observed = new List<(Guid RunId, Guid AttemptId)>();
        for (var index = 0; index < 2; index++)
        {
            var (runId, attemptId) = await SeedRunningAttemptAsync(harness);
            var registered = await harness.SendAsync(new RegisterAgentSupervisionCommand(runId, attemptId, host.Id, Guid.NewGuid()));
            Assert.True(registered.IsSuccess);
            observed.Add((runId, attemptId));
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        var release = await HoldWriteLockAsync(harness, TimeSpan.FromSeconds(8));
        var blockedScopes = new TrackingScopeFactory(harness.Provider.GetRequiredService<IServiceScopeFactory>());
        var blocked = AgentSupervisionHostedSupport.CreateSweep(harness.Provider, blockedScopes);
        await blocked.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        var stopWatch = Stopwatch.StartNew();
        await AgentSupervisionHostedSupport.StopAsync(blocked);
        var stopElapsed = stopWatch.Elapsed;
        await release;

        Assert.True(stopElapsed < Tolerated, $"The blocked sweep took {stopElapsed.TotalMilliseconds:F0} ms to stop.");

        var sweepScopes = new TrackingScopeFactory(harness.Provider.GetRequiredService<IServiceScopeFactory>());
        var sweep = AgentSupervisionHostedSupport.CreateSweep(harness.Provider, sweepScopes);
        await sweep.StartAsync(CancellationToken.None);
        foreach (var (_, attemptId) in observed)
        {
            var marked = await AgentSupervisionHostedSupport.WaitAsync(async () =>
            {
                var receipt = await AgentSupervisionHostedSupport.ReceiptAsync(harness.Provider, attemptId);
                return receipt is { AttentionReason: not null } ? receipt : null;
            });
            Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, marked.AttentionReason);
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        await AgentSupervisionHostedSupport.StopAsync(sweep);
        foreach (var (runId, attemptId) in observed)
        {
            Assert.Equal(
                [Registered, Attention],
                (await AgentSupervisionHostedSupport.SupervisionEventsAsync(harness.Provider, runId)).Select(runEvent => runEvent.EventType));
            await AssertAttemptUntouchedAsync(harness, attemptId);
        }

        // The bounded stop deliberately leaves a call that outlived it running in its own scope until it settles. The harness deletes the
        // database next, so the test proves, from the stopped sweeps' own pending Tasks, that no such call (and no connection) is still alive.
        await SweepCompletion.WaitForSettledAsync(blocked, TeardownBound);
        await SweepCompletion.WaitForSettledAsync(sweep, TeardownBound);
        Assert.Equal(0, blockedScopes.Active);
        Assert.Equal(0, sweepScopes.Active);
    }
}
