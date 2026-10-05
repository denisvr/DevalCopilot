using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AccountUsageStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The advisory account-usage warning evaluation (ADR-0026), proved at its narrowest boundary: every reported window is
/// evaluated and equality reaches the warning; a provider reached-limit state warns whatever the percentages are; evidence that is
/// not complete and current is Unavailable and never below, never a valid subset; Below only means no reported window reached the
/// saved advisory threshold. The evaluation is independent of the account-usage stop.</summary>
public sealed class CodexAccountUsageWarningPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CodexAccountUsageWarningEvaluation Evaluate(
        AccountUsageObservation? observation,
        int threshold = 80,
        DateTimeOffset? started = null,
        DateTimeOffset? completed = null,
        DateTimeOffset? now = null) =>
        CodexAccountUsageWarningPolicy.Evaluate(threshold, observation, started ?? Now, completed ?? Now, now ?? Now);

    [Theory]
    [InlineData(0, CodexAccountUsageWarningCheckState.Below)]
    [InlineData(79, CodexAccountUsageWarningCheckState.Below)]
    [InlineData(80, CodexAccountUsageWarningCheckState.Reached)]
    [InlineData(81, CodexAccountUsageWarningCheckState.Reached)]
    [InlineData(100, CodexAccountUsageWarningCheckState.Reached)]
    public void A_primary_window_reaches_the_warning_at_equality_and_above(int used, CodexAccountUsageWarningCheckState expected)
    {
        var evaluation = Evaluate(Observation(Now, used));

        Assert.Equal(expected, evaluation.State);
        Assert.Equal(Now, evaluation.ObservedAtUtc);
        Assert.Equal(
            expected == CodexAccountUsageWarningCheckState.Reached ? CodexAccountUsageWarningReason.ThresholdReached : null,
            evaluation.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void The_threshold_range_is_inclusive_at_both_ends(int threshold)
    {
        Assert.Equal(CodexAccountUsageWarningCheckState.Reached, Evaluate(Observation(Now, threshold), threshold).State);
        Assert.Equal(CodexAccountUsageWarningCheckState.Below, Evaluate(Observation(Now, threshold - 1), threshold).State);
    }

    [Fact]
    public void Every_window_of_every_bucket_is_evaluated_so_one_high_secondary_window_warns()
    {
        var observation = AccountUsageObservation.Create(
            Now,
            [
                new AccountUsageBucket("a", new AccountUsageWindow(1, null), new AccountUsageWindow(2, null)),
                new AccountUsageBucket("b", new AccountUsageWindow(3, null), new AccountUsageWindow(80, null)),
                new AccountUsageBucket("c", new AccountUsageWindow(4, null), null),
            ],
            providerReportedLimitReached: false);

        var evaluation = Evaluate(observation);

        Assert.Equal(CodexAccountUsageWarningCheckState.Reached, evaluation.State);
        Assert.Equal(5, evaluation.Windows.Count);
        var reached = Assert.Single(evaluation.Windows, window => window.ReachedThreshold);
        Assert.Equal(("b", CodexAccountUsageWarningWindowKind.Secondary, 80), (reached.BucketId, reached.Kind, reached.UsedPercent));
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

        Assert.Equal(CodexAccountUsageWarningCheckState.Below, Evaluate(observation).State);
    }

    [Fact]
    public void A_provider_reported_reached_state_warns_even_with_low_percentages()
    {
        var evaluation = Evaluate(Observation(Now, 1, 1, providerReached: true));

        Assert.Equal(CodexAccountUsageWarningCheckState.Reached, evaluation.State);
        Assert.Equal(CodexAccountUsageWarningReason.ProviderReportedLimitReached, evaluation.Reason);
        Assert.True(evaluation.ProviderReportedLimitReached);
        Assert.DoesNotContain(evaluation.Windows, window => window.ReachedThreshold);
    }

    [Fact]
    public void The_threshold_reason_wins_when_both_a_window_and_the_provider_state_reach()
    {
        var evaluation = Evaluate(Observation(Now, 90, providerReached: true));

        Assert.Equal(CodexAccountUsageWarningReason.ThresholdReached, evaluation.Reason);
    }

    [Fact]
    public void An_unavailable_or_missing_observation_is_unavailable_never_below_and_carries_nothing()
    {
        foreach (var evaluation in new[] { Evaluate(AccountUsageObservation.Unavailable), Evaluate(null) })
        {
            Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, evaluation.State);
            Assert.Equal(CodexAccountUsageWarningReason.EvidenceUnavailable, evaluation.Reason);
            Assert.Null(evaluation.ObservedAtUtc);
            Assert.Empty(evaluation.Windows);
        }
    }

    [Fact]
    public void A_partial_observation_is_unavailable_never_a_valid_subset()
    {
        var partial = AccountUsageObservation.Create(
            Now,
            [
                new AccountUsageBucket("a", new AccountUsageWindow(99, null), null),
                new AccountUsageBucket("b", null, null),
            ],
            providerReportedLimitReached: false);
        var duplicate = AccountUsageObservation.Create(
            Now,
            [
                new AccountUsageBucket("a", new AccountUsageWindow(99, null), null),
                new AccountUsageBucket("a", new AccountUsageWindow(1, null), null),
            ],
            providerReportedLimitReached: false);
        var outOfRange = AccountUsageObservation.Create(
            Now, [new AccountUsageBucket("a", new AccountUsageWindow(101, null), null)], providerReportedLimitReached: false);

        foreach (var observation in new[] { partial, duplicate, outOfRange })
        {
            var evaluation = Evaluate(observation);

            Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, evaluation.State);
            Assert.Empty(evaluation.Windows);
        }
    }

    [Fact]
    public void A_retrieval_instant_outside_the_read_interval_is_expired_evidence()
    {
        var started = Now.AddSeconds(-2);
        var completed = Now.AddSeconds(-1);

        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, Evaluate(Observation(Now, 1), 80, started, completed).Reason);
        Assert.Equal(
            CodexAccountUsageWarningReason.EvidenceExpired,
            Evaluate(Observation(started.AddSeconds(-1), 1), 80, started, completed).Reason);
    }

    [Fact]
    public void A_future_dated_retrieval_is_expired_evidence()
    {
        var future = Now.AddSeconds(5);

        var evaluation = Evaluate(Observation(future, 1), 80, Now, future, now: Now);

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, evaluation.State);
        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, evaluation.Reason);
    }

    [Theory]
    [InlineData(29, CodexAccountUsageWarningCheckState.Below)]
    [InlineData(30, CodexAccountUsageWarningCheckState.Below)]
    [InlineData(31, CodexAccountUsageWarningCheckState.Unavailable)]
    public void Evidence_is_current_for_at_most_thirty_seconds_at_evaluation(int ageSeconds, CodexAccountUsageWarningCheckState expected)
    {
        var evaluation = Evaluate(Observation(Now, 1), now: Now.AddSeconds(ageSeconds));

        Assert.Equal(expected, evaluation.State);
        if (expected == CodexAccountUsageWarningCheckState.Unavailable)
        {
            Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, evaluation.Reason);
            Assert.Empty(evaluation.Windows);
        }
    }

    [Fact]
    public void A_known_reset_that_has_passed_makes_even_a_reached_reading_unavailable_not_zero()
    {
        var evaluation = Evaluate(Observation(Now, 99, primaryReset: Now));

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, evaluation.State);
        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, evaluation.Reason);
    }

    [Fact]
    public void A_reset_still_in_the_future_changes_nothing()
    {
        Assert.Equal(CodexAccountUsageWarningCheckState.Below, Evaluate(Observation(Now, 1, primaryReset: Now.AddSeconds(1))).State);
        Assert.Equal(
            CodexAccountUsageWarningCheckState.Reached, Evaluate(Observation(Now, 90, primaryReset: Now.AddHours(1))).State);
    }

    [Fact]
    public void An_inverted_read_interval_is_expired_evidence()
    {
        var evaluation = Evaluate(Observation(Now, 1), 80, Now.AddSeconds(1), Now);

        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, evaluation.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void An_invalid_threshold_is_never_evaluated(int threshold)
    {
        var evaluation = Evaluate(Observation(Now, 1), threshold);

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, evaluation.State);
    }

    [Fact]
    public void Evaluation_carries_only_bounded_identifiers_percentages_and_the_host_retrieval_time()
    {
        var evaluation = Evaluate(Observation(Now, 90, 10, providerReached: true));

        Assert.Equal(Now, evaluation.ObservedAtUtc);
        Assert.Equal(
            [("codex", CodexAccountUsageWarningWindowKind.Primary, 90, true), ("codex", CodexAccountUsageWarningWindowKind.Secondary, 10, false)],
            evaluation.Windows.Select(window => (window.BucketId, window.Kind, window.UsedPercent, window.ReachedThreshold)));
    }
}
