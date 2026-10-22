using System.Reflection;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Sdk;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>LCQ-V2 follow-up: the completion proof of a stopped sweep must hold while a call is paused BEFORE it enters the scope factory, which a
/// scope counter cannot see. The gate sits at scope acquisition; the helper is invoked directly and must stay pending until the gate is released.</summary>
public sealed class SweepCompletionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static Task? PendingInventoryOf(AgentSupervisionSweep sweep) =>
        (Task?)typeof(AgentSupervisionSweep).GetField("pendingInventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sweep);

    [Fact]
    public async Task The_completion_proof_stays_pending_while_a_stopped_sweeps_call_is_gated_before_its_scope_exists_and_completes_once_released()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        await harness.MigrateAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var gated = new GatedScopeFactory(harness.Provider.GetRequiredService<IServiceScopeFactory>(), entered, release);
        var scopes = new TrackingScopeFactory(gated);
        using var sweep = AgentSupervisionHostedSupport.CreateSweep(harness.Provider, scopes);
        await sweep.StartAsync(CancellationToken.None);
        Task? pending = null;
        Task? completion = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The real sweep must enter scope creation.");
            await AgentSupervisionHostedSupport.StopAsync(sweep);

            // The call is paused before any scope exists: the scope count is zero, yet the call is alive.
            pending = PendingInventoryOf(sweep);
            Assert.NotNull(pending);
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, scopes.Active);

            // The helper is invoked directly (no wrapper that has not started), and it must still be waiting after a real interval.
            completion = SweepCompletion.WaitForSettledAsync(sweep, Bound);
            var raced = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.NotSame(completion, raced);
            Assert.False(completion.IsCompleted, "The completion proof returned while the stopped sweep still had a gated call.");
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        Assert.NotNull(completion);
        await completion.WaitAsync(Bound);
        Assert.True(pending!.IsCompleted);
    }

    [Fact]
    public async Task The_completion_proof_fails_explicitly_for_a_sweep_that_never_started_or_whose_loop_has_not_ended()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        await harness.MigrateAsync();
        using var sweep = AgentSupervisionHostedSupport.CreateSweep(harness.Provider);

        await Assert.ThrowsAsync<XunitException>(() => SweepCompletion.WaitForSettledAsync(sweep, TimeSpan.FromSeconds(1)));

        await sweep.StartAsync(CancellationToken.None);
        try
        {
            Assert.Throws<XunitException>(() => SweepCompletion.SnapshotPending(sweep));
        }
        finally
        {
            await AgentSupervisionHostedSupport.StopAsync(sweep);
        }
    }

    [Fact]
    public async Task The_completion_proof_never_falls_back_to_a_zero_count_when_the_private_bookkeeping_is_missing()
    {
        using var unrelated = new UnrelatedService();
        await unrelated.StartAsync(CancellationToken.None);
        await unrelated.StopAsync(CancellationToken.None);

        var failure = Assert.Throws<XunitException>(() => SweepCompletion.SnapshotPending(unrelated));

        Assert.Contains("pendingInventory", failure.Message, StringComparison.Ordinal);
    }

    private sealed class UnrelatedService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class GatedScopeFactory(IServiceScopeFactory inner, ManualResetEventSlim entered, ManualResetEventSlim release) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The test-owned scope gate was not released.");
            }

            return inner.CreateScope();
        }
    }
}
