using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AccountUsageStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The one account-usage stop policy (ADR-0025), proved at its narrowest boundary: every reported window is evaluated and
/// equality reaches the stop; a provider reached-limit state stops whatever the percentages are; evidence that is not complete and
/// current is never read as below the threshold; below the threshold only means this one local guard did not stop.</summary>
public sealed class CodexAccountUsageStopPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly CodexAccountUsageLaunchTuple Tuple = new(@"C:\safe\codex.exe", null);

    private static CodexAccountUsageGuardFacts Facts(
        AccountUsageObservation observation, int threshold = 80, DateTimeOffset? started = null, DateTimeOffset? completed = null) =>
        new(null, threshold, Tuple.ExecutablePath, Tuple.ScriptPath, observation, started ?? Now, completed ?? Now);

    private static CodexAccountUsageStopEvaluation Evaluate(CodexAccountUsageGuardFacts? facts, int threshold = 80, DateTimeOffset? now = null) =>
        CodexAccountUsageStopPolicy.Evaluate(facts, threshold, now ?? Now);

    [Theory]
    [InlineData(0, true)]
    [InlineData(79, true)]
    [InlineData(80, false)]
    [InlineData(81, false)]
    [InlineData(100, false)]
    public void A_primary_window_permits_only_strictly_below_the_threshold(int used, bool permits)
    {
        var evaluation = Evaluate(Facts(Observation(Now, used)));

        Assert.Equal(permits, evaluation.Permits);
        if (!permits)
        {
            Assert.Equal(CodexAccountUsageDecisionReason.ThresholdReached, evaluation.StopDecision!.Reason);
            Assert.Equal(CodexAccountUsageDecisionKind.Reached, evaluation.StopDecision.Kind);
        }
    }

    [Fact]
    public void Every_window_of_every_bucket_is_evaluated_so_one_high_secondary_window_stops()
    {
        var observation = AccountUsageObservation.Create(
            Now,
            [
                new AccountUsageBucket("a", new AccountUsageWindow(1, null), new AccountUsageWindow(2, null)),
                new AccountUsageBucket("b", new AccountUsageWindow(3, null), new AccountUsageWindow(80, null)),
                new AccountUsageBucket("c", new AccountUsageWindow(4, null), null),
            ],
            providerReportedLimitReached: false);

        var evaluation = Evaluate(Facts(observation));

        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdReached, evaluation.StopDecision!.Reason);
        Assert.Equal(5, evaluation.StopDecision.Windows.Length);
        Assert.Contains(evaluation.StopDecision.Windows, window => window.BucketId == "b" && window.Window == CodexAccountUsageWindowKind.Secondary);
    }

    [Fact]
    public void Windows_are_never_summed_or_averaged()
    {
        var observation = AccountUsageObservation.Create(
            Now,
            [
                new AccountUsageBucket("a", new AccountUsageWindow(50, null), new AccountUsageWindow(50, null)),
                new AccountUsageBucket("b", new AccountUsageWindow(50, null), null),
            ],
            providerReportedLimitReached: false);

        Assert.True(Evaluate(Facts(observation)).Permits);
    }

    [Fact]
    public void A_provider_reported_reached_state_stops_even_with_low_percentages()
    {
        var evaluation = Evaluate(Facts(Observation(Now, 1, 1, providerReached: true)));

        Assert.Equal(CodexAccountUsageDecisionKind.Reached, evaluation.StopDecision!.Kind);
        Assert.Equal(CodexAccountUsageDecisionReason.ProviderReportedLimitReached, evaluation.StopDecision.Reason);
    }

    [Fact]
    public void The_threshold_reason_wins_when_both_a_window_and_the_provider_state_reach()
    {
        var evaluation = Evaluate(Facts(Observation(Now, 90, providerReached: true)));

        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdReached, evaluation.StopDecision!.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void The_threshold_range_is_inclusive_at_both_ends(int threshold)
    {
        Assert.False(Evaluate(Facts(Observation(Now, threshold), threshold), threshold).Permits);
        Assert.True(Evaluate(Facts(Observation(Now, threshold - 1), threshold), threshold).Permits);
    }

    [Fact]
    public void An_unavailable_observation_is_an_unavailable_decision_never_below_the_threshold()
    {
        var evaluation = Evaluate(Facts(AccountUsageObservation.Unavailable));

        Assert.Equal(CodexAccountUsageDecisionKind.Unavailable, evaluation.StopDecision!.Kind);
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceUnavailable, evaluation.StopDecision.Reason);
        Assert.Empty(evaluation.StopDecision.Windows);
        Assert.Equal(80, evaluation.StopDecision.ThresholdPercent);
    }

    [Fact]
    public void Missing_facts_and_facts_for_another_threshold_stop_as_unavailable()
    {
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceUnavailable, Evaluate(null).StopDecision!.Reason);
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceUnavailable, Evaluate(Facts(Observation(Now, 1), threshold: 50), threshold: 80).StopDecision!.Reason);
    }

    [Fact]
    public void A_retrieval_instant_outside_the_read_interval_is_expired_evidence()
    {
        var started = Now.AddSeconds(-2);
        var completed = Now.AddSeconds(-1);

        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, Evaluate(Facts(Observation(Now, 1), 80, started, completed)).StopDecision!.Reason);
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, Evaluate(Facts(Observation(started.AddSeconds(-1), 1), 80, started, completed)).StopDecision!.Reason);
    }

    [Fact]
    public void A_future_dated_retrieval_is_expired_evidence()
    {
        var future = Now.AddSeconds(5);

        var evaluation = Evaluate(Facts(Observation(future, 1), 80, Now, future), now: Now);

        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, evaluation.StopDecision!.Reason);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Evidence_is_current_for_at_most_thirty_seconds_at_the_commit_seam(int ageSeconds, bool permits)
    {
        var evaluation = Evaluate(Facts(Observation(Now, 1)), now: Now.AddSeconds(ageSeconds));

        Assert.Equal(permits, evaluation.Permits);
        if (!permits)
        {
            Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, evaluation.StopDecision!.Reason);
            Assert.Equal(Now, evaluation.StopDecision.RetrievedAtUtc);
        }
    }

    [Fact]
    public void A_known_reset_that_has_passed_makes_the_evidence_unavailable_not_zero()
    {
        var evaluation = Evaluate(Facts(Observation(Now, 99, primaryReset: Now)));

        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, evaluation.StopDecision!.Reason);
        Assert.Equal(CodexAccountUsageDecisionKind.Unavailable, evaluation.StopDecision.Kind);
    }

    [Fact]
    public void A_reset_still_in_the_future_changes_nothing_and_a_reached_percentage_still_stops()
    {
        Assert.True(Evaluate(Facts(Observation(Now, 1, primaryReset: Now.AddSeconds(1)))).Permits);
        Assert.Equal(
            CodexAccountUsageDecisionReason.ThresholdReached,
            Evaluate(Facts(Observation(Now, 90, primaryReset: Now.AddHours(1)))).StopDecision!.Reason);
    }

    [Fact]
    public void An_inverted_read_interval_is_expired_evidence()
    {
        var evaluation = Evaluate(Facts(Observation(Now, 1), 80, Now.AddSeconds(1), Now));

        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, evaluation.StopDecision!.Reason);
    }

    [Fact]
    public void The_unusable_threshold_decision_carries_no_number_and_no_observation()
    {
        var decision = CodexAccountUsageStopPolicy.UnusableThreshold();

        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdUnusable, decision.Reason);
        Assert.Null(decision.ThresholdPercent);
        Assert.Null(decision.RetrievedAtUtc);
        Assert.Empty(decision.Windows);
    }

    [Fact]
    public void A_decision_serializes_without_any_provider_text_or_account_identity()
    {
        var observation = AccountUsageObservation.Create(
            Now, [new AccountUsageBucket("codex", new AccountUsageWindow(90, null), null)], providerReportedLimitReached: true);

        var text = Evaluate(Facts(observation)).StopDecision!.Serialize();

        Assert.Equal(
            "{\"version\":1,\"source\":\"codex-account-rate-limits-v1\",\"decision\":\"reached\",\"reason\":\"threshold_reached\","
            + "\"thresholdPercent\":80,\"retrievedAtUtc\":\"2026-10-04T12:00:00.0000000Z\","
            + "\"windows\":[{\"bucket\":\"codex\",\"window\":\"primary\",\"usedPercent\":90}]}",
            text);
    }
}
