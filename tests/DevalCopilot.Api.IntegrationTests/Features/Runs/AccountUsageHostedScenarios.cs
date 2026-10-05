using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public static class AccountUsageHostedScenarios
{
    public const int Threshold = 80;

    /// <summary>The attempt, its completion events and its collaboration messages, read through a fresh scope of the host.</summary>
    public static async Task<HostedAttemptView> ReadViewAsync(IServiceProvider provider, Guid attemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        var events = await db.Events.AsNoTracking()
            .CountAsync(candidate => candidate.AttemptId == attemptId && candidate.EventType == RunEventType.AgentAttemptCompleted);
        var messages = await db.CollaborationMessages.AsNoTracking().CountAsync(candidate => candidate.AttemptId == attemptId);
        return new HostedAttemptView(attempt, events, messages);
    }

    /// <summary>Runs one statement against the host's database through a fresh scope (a test-only fault injection).</summary>
    public static async Task ExecuteSqlAsync(IServiceProvider provider, string sql)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    private static AccountUsageObservation At(int primary, int? secondary = null) =>
        AccountUsageObservation.Create(
            DateTimeOffset.UtcNow,
            [new AccountUsageBucket("codex", new AccountUsageWindow(primary, null), secondary is { } value ? new AccountUsageWindow(value, null) : null)],
            providerReportedLimitReached: false);

    private static async Task<HostedAttemptView> WaitUntilTerminalAsync(AccountUsageHostedHarness harness, Guid attemptId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        HostedAttemptView view;
        do
        {
            await Task.Delay(50);
            view = await harness.ReadAsync(attemptId);
        }
        while (view.Status == AttemptStatus.Running && DateTimeOffset.UtcNow < deadline);

        return view;
    }

    private static async Task RunSupervisorAsync(BackgroundService supervisor, Func<Task> whileRunning)
    {
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await whileRunning();
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await supervisor.StopAsync(stop.Token);
        }
    }

    /// <summary>Below the stop at the claim, then a stop at dispatch: one terminal outcome and event, a recorded decision, no dispatch
    /// marker, no provider invocation, and neither more observations nor another event after further polling and a restart.</summary>
    public static async Task A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(
        AccountUsageHostedHarness harness, bool unavailable)
    {
        var adapter = harness.Adapter;
        adapter.Respond = call => call == 0 ? At(10) : unavailable ? AccountUsageObservation.Unavailable : At(90, 3);
        var (runId, attemptId) = await harness.ClaimAsync();
        Assert.Equal(1, adapter.Calls);
        Assert.Equal(AttemptStatus.Running, (await harness.ReadAsync(attemptId)).Status);

        HostedAttemptView view = null!;
        await RunSupervisorAsync(harness.NewSupervisor(), async () =>
        {
            view = await WaitUntilTerminalAsync(harness, attemptId);
            await Task.Delay(TimeSpan.FromMilliseconds(1600));
        });

        view = await harness.ReadAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, view.Status);
        Assert.Equal(unavailable ? AgentOutcome.AccountUsageEvidenceUnavailable : AgentOutcome.AccountUsageStopReached, view.Attempt.AgentOutcome);
        Assert.Null(view.Attempt.AgentDispatchedAtUtc);
        Assert.Null(view.Attempt.GetAgentProcessExecutionEvidence());
        Assert.Equal(0, harness.AgentInvocations());
        Assert.Equal(2, adapter.Calls);
        Assert.Equal(1, view.CompletionEvents);
        Assert.Equal(0, view.Messages);
        var decision = view.Attempt.GetAgentAccountUsageDecision()!;
        Assert.Equal(Threshold, decision.ThresholdPercent);
        Assert.Equal(unavailable ? CodexAccountUsageDecisionReason.EvidenceUnavailable : CodexAccountUsageDecisionReason.ThresholdReached, decision.Reason);
        if (!unavailable)
        {
            Assert.Equal([90, 3], decision.Windows.Select(window => window.UsedPercent));
        }

        // A restarted supervisor never redispatches or re-observes it.
        await RunSupervisorAsync(harness.NewSupervisor(), () => Task.Delay(TimeSpan.FromMilliseconds(1600)));
        var restarted = await harness.ReadAsync(attemptId);
        Assert.Equal(2, adapter.Calls);
        Assert.Equal(0, harness.AgentInvocations());
        Assert.Equal(1, restarted.CompletionEvents);
        Assert.Equal(AttemptStatus.Failed, restarted.Status);
        Assert.Equal(runId, restarted.Attempt.RunId);
    }

    /// <summary>A stop exactly at the threshold at dispatch also stops it (equality), through the real supervisor.</summary>
    public static async Task A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt(AccountUsageHostedHarness harness)
    {
        harness.Adapter.Respond = call => call == 0 ? At(Threshold - 1) : At(Threshold);
        var (_, attemptId) = await harness.ClaimAsync();

        await RunSupervisorAsync(harness.NewSupervisor(), async () => await WaitUntilTerminalAsync(harness, attemptId));

        var view = await harness.ReadAsync(attemptId);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, view.Attempt.AgentOutcome);
        Assert.Equal(0, harness.AgentInvocations());
    }

    /// <summary>Below the stop at both seams: the attempt dispatches and the provider runs exactly once.</summary>
    public static async Task A_healthy_account_dispatches_and_the_provider_runs_once(AccountUsageHostedHarness harness)
    {
        var adapter = harness.Adapter;
        adapter.Respond = _ => At(10, 20);
        var (_, attemptId) = await harness.ClaimAsync();

        HostedAttemptView view = null!;
        await RunSupervisorAsync(harness.NewSupervisor(), async () =>
        {
            view = await WaitUntilTerminalAsync(harness, attemptId);
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
        });

        view = await harness.ReadAsync(attemptId);
        Assert.NotNull(view.Attempt.AgentDispatchedAtUtc);
        Assert.NotEqual(AttemptStatus.Running, view.Status);
        Assert.Equal(1, harness.AgentInvocations());
        Assert.Equal(2, adapter.Calls);
        Assert.Equal(Threshold, view.Attempt.ReadAgentCodexAccountUsageStopPercent().Value);
        Assert.Null(view.Attempt.AgentAccountUsageDecisionSnapshot);
    }

    /// <summary>Below the stop at the claim, a stop at dispatch whose terminal recording cannot commit (a real database fault on the
    /// completion event), then below-threshold observations forever: the attempt stays Running and undispatched, is never re-observed
    /// or dispatched by ordinary polling or a restarted supervisor, no provider is ever invoked and no event exists. When the fault is
    /// gone, nothing resumes it either (no automatic recovery authority); the blocker is only surfaced.</summary>
    public static async Task A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it(
        AccountUsageHostedHarness harness)
    {
        var adapter = harness.Adapter;
        adapter.Respond = call => call == 0 ? At(10) : call == 1 ? At(90, 3) : At(10);
        var (_, attemptId) = await harness.ClaimAsync();
        await harness.ExecuteSqlAsync(
            "CREATE TRIGGER fail_account_usage_completion BEFORE INSERT ON events BEGIN SELECT RAISE(ABORT, 'injected recording fault'); END");

        await RunSupervisorAsync(harness.NewSupervisor(), () => Task.Delay(TimeSpan.FromMilliseconds(2500)));

        var view = await harness.ReadAsync(attemptId);
        Assert.Equal(AttemptStatus.Running, view.Status);
        Assert.Null(view.Attempt.AgentDispatchedAtUtc);
        Assert.Null(view.Attempt.AgentAccountUsageDecisionSnapshot);
        Assert.Equal(0, harness.AgentInvocations());
        Assert.Equal(2, adapter.Calls);
        Assert.Equal(0, view.CompletionEvents);

        // The fault is removed and a new supervisor reuses the same host singleton: still no re-observation, dispatch or completion.
        await harness.ExecuteSqlAsync("DROP TRIGGER fail_account_usage_completion");
        await RunSupervisorAsync(harness.NewSupervisor(), () => Task.Delay(TimeSpan.FromMilliseconds(2500)));

        var later = await harness.ReadAsync(attemptId);
        Assert.Equal(AttemptStatus.Running, later.Status);
        Assert.Null(later.Attempt.AgentDispatchedAtUtc);
        Assert.Equal(0, harness.AgentInvocations());
        Assert.Equal(2, adapter.Calls);
        Assert.Equal(0, later.CompletionEvents);
    }
}
