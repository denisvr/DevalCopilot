using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The account-usage claim contract, run by each of the four Codex claim handlers' test classes.</summary>
public static class AccountUsageClaimContract
{
    private static void AssertNothingClaimed(AccountUsageClaimOutcome outcome, int attemptsBefore, int attemptsAfter)
    {
        Assert.False(outcome.Success);
        Assert.Equal(attemptsBefore, attemptsAfter);
    }

    private static async Task<(Guid RunId, int Attempts)> ConfiguredAsync(AccountUsageClaimHarness h, int? percent = 80)
    {
        var runId = await h.SeedRunAsync();
        await AccountUsageStopTestSupport.SetStopAsync(h.Fixture, runId, percent);
        return (runId, await h.AttemptCountAsync(runId));
    }

    public static async Task A_secondary_window_at_the_threshold_refuses_before_any_external_work(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 10, secondary: 80));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.Equal(CodexAccountUsageStopGate.ReachedCode, outcome.ErrorCode);
        Assert.Equal(0, outcome.GitCaptures);
        Assert.Equal(0, outcome.OrphanedManifests);
        Assert.Equal(1, adapter.Calls);
        Assert.Equal((AccountUsageClaimHarness.LaunchExecutable, (string?)null), Assert.Single(adapter.Tuples));
        AssertNothingClaimed(outcome, before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_primary_window_at_the_threshold_refuses(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);

        var outcome = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 80)));

        Assert.Equal(CodexAccountUsageStopGate.ReachedCode, outcome.ErrorCode);
        AssertNothingClaimed(outcome, before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_provider_reported_reached_state_refuses_even_with_low_percentages(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);

        var outcome = await h.ClaimAsync(
            runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1, secondary: 1, providerReached: true)));

        Assert.Equal(CodexAccountUsageStopGate.ReachedCode, outcome.ErrorCode);
        AssertNothingClaimed(outcome, before, await h.AttemptCountAsync(runId));
    }

    public static async Task An_unavailable_observation_refuses_without_a_partial_subset(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);

        var outcome = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageObservation.Unavailable));

        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, outcome.ErrorCode);
        Assert.Equal(0, outcome.GitCaptures);
        AssertNothingClaimed(outcome, before, await h.AttemptCountAsync(runId));
    }

    public static async Task Evidence_outside_the_read_interval_or_with_a_passed_reset_refuses(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);

        var before1 = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now.AddSeconds(-1), primary: 1)));
        var future = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now.AddSeconds(1), primary: 1)));
        var reset = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1, primaryReset: h.Now)));

        Assert.All(new[] { before1, future, reset }, outcome => Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, outcome.ErrorCode));
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_value_just_below_the_threshold_is_permitted_and_snapshotted_on_the_attempt(AccountUsageClaimHarness h)
    {
        var (runId, _) = await ConfiguredAsync(h);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 79, secondary: 79));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(1, adapter.Calls);
        var attempt = await AccountUsageStopTestSupport.NewestAttemptAsync(h.Fixture, runId);
        Assert.Equal(80, attempt!.ReadAgentCodexAccountUsageStopPercent().Value);
        Assert.Null(attempt.AgentAccountUsageDecisionSnapshot);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    public static async Task A_later_setting_change_does_not_alter_the_claimed_attempts_snapshot(AccountUsageClaimHarness h)
    {
        var (runId, _) = await ConfiguredAsync(h, 80);
        var outcome = await h.ClaimAsync(runId, StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1)));
        Assert.True(outcome.Success, outcome.ErrorCode);

        await AccountUsageStopTestSupport.SetStopAsync(h.Fixture, runId, 5);

        Assert.Equal(80, (await AccountUsageStopTestSupport.NewestAttemptAsync(h.Fixture, runId))!.ReadAgentCodexAccountUsageStopPercent().Value);
    }

    public static async Task A_disabled_stop_makes_no_observation_and_records_no_snapshot(AccountUsageClaimHarness h)
    {
        var runId = await h.SeedRunAsync();
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 100));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(0, adapter.Calls);
        Assert.True((await AccountUsageStopTestSupport.NewestAttemptAsync(h.Fixture, runId))!.ReadAgentCodexAccountUsageStopPercent().IsAbsent);
    }

    public static async Task A_cleared_stop_is_disabled_again(AccountUsageClaimHarness h)
    {
        var (runId, _) = await ConfiguredAsync(h, 80);
        await AccountUsageStopTestSupport.SetStopAsync(h.Fixture, runId, null);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 100));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(0, adapter.Calls);
    }

    public static async Task A_malformed_stored_setting_refuses_without_an_observation(AccountUsageClaimHarness h, object stored)
    {
        var runId = await h.SeedRunAsync();
        await AccountUsageStopTestSupport.SetStoredAsync(h.Fixture, runId, stored);
        var before = await h.AttemptCountAsync(runId);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.Equal(CodexAccountUsageStopGate.SettingInvalidCode, outcome.ErrorCode);
        Assert.Equal(0, adapter.Calls);
        AssertNothingClaimed(outcome, before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_missing_observation_capability_or_a_throwing_adapter_refuses_never_permits(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);
        var throwing = new StubAccountUsageAdapter((_, _, _, _) => throw new InvalidOperationException("boom"));

        var missing = await h.ClaimAsync(runId, adapter: null);
        var thrown = await h.ClaimAsync(runId, throwing);

        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, missing.ErrorCode);
        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, thrown.ErrorCode);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_missing_launch_target_refuses_without_an_observation(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);
        await using (var context = h.Fixture.CreateContext())
        {
            await context.HostCapabilitySnapshots.ExecuteDeleteAsync();
        }

        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));
        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.False(outcome.Success);
        Assert.Equal(0, adapter.Calls);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_setting_changed_between_the_check_and_the_commit_refuses_and_commits_nothing(AccountUsageClaimHarness h, int? changeTo)
    {
        var (runId, before) = await ConfiguredAsync(h);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(runId, adapter, async _ => await AccountUsageStopTestSupport.SetStopAsync(h.Fixture, runId, changeTo));

        Assert.Equal(CodexAccountUsageStopGate.PolicyChangedCode, outcome.ErrorCode);
        Assert.Equal(1, outcome.OrphanedManifests);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_stop_enabled_between_the_check_and_the_commit_cannot_be_bypassed(AccountUsageClaimHarness h)
    {
        var runId = await h.SeedRunAsync();
        var before = await h.AttemptCountAsync(runId);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 100));

        var outcome = await h.ClaimAsync(runId, adapter, async _ => await AccountUsageStopTestSupport.SetStopAsync(h.Fixture, runId, 50));

        Assert.Equal(CodexAccountUsageStopGate.PolicyChangedCode, outcome.ErrorCode);
        Assert.Equal(0, adapter.Calls);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task A_launch_target_changed_between_the_check_and_the_commit_refuses_and_commits_nothing(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(
            runId, adapter, async _ => await AccountUsageStopTestSupport.ChangeLaunchTargetAsync(h.Fixture, @"C:\safe\other-codex.exe"));

        Assert.Equal(CodexAccountUsageStopGate.PolicyChangedCode, outcome.ErrorCode);
        Assert.Equal(1, outcome.OrphanedManifests);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task Evidence_that_is_stale_at_the_commit_seam_refuses_and_commits_nothing(AccountUsageClaimHarness h)
    {
        var (runId, before) = await ConfiguredAsync(h);
        var clock = new AdjustableTimeProvider(h.Now);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(runId, adapter, clock, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            return Task.CompletedTask;
        });

        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, outcome.ErrorCode);
        Assert.Equal(1, outcome.OrphanedManifests);
        Assert.Equal(before, await h.AttemptCountAsync(runId));
    }

    public static async Task Evidence_exactly_thirty_seconds_old_at_the_commit_seam_is_still_current(AccountUsageClaimHarness h)
    {
        var (runId, _) = await ConfiguredAsync(h);
        var clock = new AdjustableTimeProvider(h.Now);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(runId, adapter, clock, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        });

        Assert.True(outcome.Success, outcome.ErrorCode);
    }

    public static async Task A_malformed_advisory_warning_storage_never_affects_the_claim_or_the_stop(AccountUsageClaimHarness h, object stored)
    {
        var (runId, _) = await ConfiguredAsync(h, 80);
        await AccountUsageStopTestSupport.SetStoredWarningAsync(h.Fixture, runId, stored);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 79));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(1, adapter.Calls);
        Assert.Equal(80, (await AccountUsageStopTestSupport.NewestAttemptAsync(h.Fixture, runId))!.ReadAgentCodexAccountUsageStopPercent().Value);
    }

    public static async Task A_reached_or_disabled_stop_is_decided_by_the_stop_alone_whatever_the_warning_says(AccountUsageClaimHarness h)
    {
        var runId = await h.SeedRunAsync();
        await AccountUsageStopTestSupport.SetWarningAsync(h.Fixture, runId, 1);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 100));

        var outcome = await h.ClaimAsync(runId, adapter);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Equal(0, adapter.Calls);
    }

    public static async Task An_advisory_warning_change_during_the_claim_neither_refuses_nor_is_overwritten(AccountUsageClaimHarness h)
    {
        var (runId, _) = await ConfiguredAsync(h, 80);
        var adapter = StubAccountUsageAdapter.Always(AccountUsageStopTestSupport.Observation(h.Now, primary: 1));

        var outcome = await h.ClaimAsync(runId, adapter, async _ => await AccountUsageStopTestSupport.SetWarningAsync(h.Fixture, runId, 7));

        Assert.True(outcome.Success, outcome.ErrorCode);
        await using var context = h.Fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(7, run.ReadCodexAccountUsageWarningPercent().Value);
        Assert.Equal(80, run.ReadCodexAccountUsageStopPercent().Value);
    }
}
