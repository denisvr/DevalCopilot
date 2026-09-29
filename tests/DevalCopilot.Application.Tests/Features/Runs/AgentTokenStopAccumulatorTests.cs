using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AgentTokenStopAccumulatorTests
{
    private static AgentTokenUsageEvidence Codex(int input, int output) => TokenStopTestSupport.CodexUsage(input, output);

    private static AgentTokenUsageEvidence Claude(int input, int output, int? creation, int? read) =>
        TokenStopTestSupport.ClaudeUsage(input, output, creation, read);

    private static AgentTokenStopEvaluation Evaluate(
        AgentProvider provider,
        long? threshold,
        params (AttemptStatus Status, AgentProvider? Provider, AgentTokenUsageEvidence? Usage)[] attempts)
    {
        var accumulator = new AgentTokenStopAccumulator();
        foreach (var (status, attemptProvider, usage) in attempts)
        {
            accumulator.Add(status, attemptProvider, usage);
        }

        return accumulator.ToEvaluation(provider, threshold);
    }

    private static (AttemptStatus, AgentProvider?, AgentTokenUsageEvidence?) Done(AgentProvider? provider, AgentTokenUsageEvidence? usage) =>
        (AttemptStatus.Completed, provider, usage);

    [Fact]
    public void Without_a_threshold_the_stop_is_not_configured_and_never_blocks_whatever_the_evidence()
    {
        var evaluation = Evaluate(AgentProvider.Codex, null, Done(AgentProvider.Codex, null), Done(null, null));

        Assert.Equal(AgentTokenStopState.NotConfigured, evaluation.State);
        Assert.False(evaluation.BlocksClaim);
        Assert.Null(evaluation.ThresholdTokens);
    }

    [Fact]
    public void No_dispatched_history_permits_the_first_claim_and_is_not_a_reported_zero_or_a_gap()
    {
        var evaluation = Evaluate(AgentProvider.ClaudeCode, 1);

        Assert.Equal(AgentTokenStopState.NoDispatchedHistory, evaluation.State);
        Assert.False(evaluation.BlocksClaim);
        Assert.Equal(0, evaluation.CountedAttempts);
        Assert.Equal(0, evaluation.PendingAttempts);
        Assert.Equal(0, evaluation.InsufficientEvidenceAttempts);
    }

    [Fact]
    public void An_unattributed_dispatched_attempt_makes_even_an_empty_provider_history_indeterminate()
    {
        var evaluation = Evaluate(AgentProvider.Codex, 100, Done(null, null));

        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, evaluation.State);
        Assert.True(evaluation.BlocksClaim);
        Assert.Equal(1, evaluation.UnattributedAttempts);
    }

    [Fact]
    public void An_undefined_provider_value_is_unattributed_never_assigned_to_a_provider()
    {
        var evaluation = Evaluate(AgentProvider.Codex, 100, Done((AgentProvider)99, Codex(1, 1)));

        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, evaluation.State);
        Assert.Equal(1, evaluation.UnattributedAttempts);
        Assert.Equal(0, evaluation.CountedAttempts);
    }

    [Theory]
    [InlineData(1199L, AgentTokenStopState.ThresholdReached)]
    [InlineData(1200L, AgentTokenStopState.ThresholdReached)]
    [InlineData(1201L, AgentTokenStopState.BelowThresholdComplete)]
    public void Codex_counts_input_plus_output_and_equality_with_the_threshold_blocks(long threshold, AgentTokenStopState expected)
    {
        var evaluation = Evaluate(AgentProvider.Codex, threshold, Done(AgentProvider.Codex, Codex(1000, 200)));

        Assert.Equal(1200, evaluation.KnownTokenCount);
        Assert.Equal(expected, evaluation.State);
        Assert.Equal(expected == AgentTokenStopState.ThresholdReached, evaluation.BlocksClaim);
    }

    [Theory]
    [InlineData(615L, AgentTokenStopState.ThresholdReached)]
    [InlineData(616L, AgentTokenStopState.BelowThresholdComplete)]
    public void Claude_counts_input_cache_creation_cache_read_and_output_each_once(long threshold, AgentTokenStopState expected)
    {
        var evaluation = Evaluate(AgentProvider.ClaudeCode, threshold, Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, 60)));

        Assert.Equal(615, evaluation.KnownTokenCount);
        Assert.Equal(expected, evaluation.State);
    }

    [Fact]
    public void Counts_are_summed_across_concluded_attempts_of_one_provider()
    {
        var evaluation = Evaluate(
            AgentProvider.Codex,
            1000,
            Done(AgentProvider.Codex, Codex(300, 100)),
            Done(AgentProvider.Codex, Codex(400, 100)));

        Assert.Equal(900, evaluation.KnownTokenCount);
        Assert.Equal(2, evaluation.CountedAttempts);
        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, evaluation.State);
    }

    [Fact]
    public void A_zero_usage_row_is_a_known_zero_distinct_from_missing_usage()
    {
        var known = Evaluate(AgentProvider.Codex, 5, Done(AgentProvider.Codex, Codex(0, 0)));
        var missing = Evaluate(AgentProvider.Codex, 5, Done(AgentProvider.Codex, null));

        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, known.State);
        Assert.Equal(0, known.KnownTokenCount);
        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, missing.State);
        Assert.Equal(0, missing.CountedAttempts);
        Assert.Equal(1, missing.InsufficientEvidenceAttempts);
    }

    [Fact]
    public void A_claude_row_missing_a_cache_count_is_insufficient_never_zero_filled()
    {
        var missingCreation = Evaluate(AgentProvider.ClaudeCode, 1_000_000, Done(AgentProvider.ClaudeCode, Claude(500, 50, null, 60)));
        var missingRead = Evaluate(AgentProvider.ClaudeCode, 1_000_000, Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, null)));

        Assert.All(new[] { missingCreation, missingRead }, evaluation =>
        {
            Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, evaluation.State);
            Assert.Equal(1, evaluation.InsufficientEvidenceAttempts);
            Assert.Equal(0, evaluation.KnownTokenCount);
        });
    }

    [Fact]
    public void A_running_attempt_is_pending_and_never_counted_even_when_its_row_looks_valid()
    {
        var evaluation = Evaluate(
            AgentProvider.Codex, 100, (AttemptStatus.Running, AgentProvider.Codex, Codex(5_000, 5_000)));

        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, evaluation.State);
        Assert.Equal(1, evaluation.PendingAttempts);
        Assert.Equal(0, evaluation.KnownTokenCount);
    }

    [Fact]
    public void A_known_count_at_the_threshold_blocks_as_reached_even_when_other_evidence_is_incomplete()
    {
        var evaluation = Evaluate(
            AgentProvider.Codex,
            100,
            Done(AgentProvider.Codex, Codex(60, 40)),
            Done(AgentProvider.Codex, null),
            (AttemptStatus.Running, AgentProvider.Codex, null),
            Done(null, null));

        Assert.Equal(AgentTokenStopState.ThresholdReached, evaluation.State);
        Assert.Equal(100, evaluation.KnownTokenCount);
        Assert.Equal(1, evaluation.InsufficientEvidenceAttempts);
        Assert.Equal(1, evaluation.PendingAttempts);
        Assert.Equal(1, evaluation.UnattributedAttempts);
    }

    [Fact]
    public void The_two_providers_are_evaluated_separately_and_never_combined()
    {
        var attempts = new[]
        {
            Done(AgentProvider.Codex, Codex(900, 100)),
            Done(AgentProvider.ClaudeCode, Claude(10, 10, 10, 10)),
        };

        var codex = Evaluate(AgentProvider.Codex, 1000, attempts);
        var claude = Evaluate(AgentProvider.ClaudeCode, 41, attempts);

        Assert.Equal(AgentTokenStopState.ThresholdReached, codex.State);
        Assert.Equal(1000, codex.KnownTokenCount);
        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, claude.State);
        Assert.Equal(40, claude.KnownTokenCount);
    }

    [Fact]
    public void Another_providers_gaps_do_not_make_this_provider_indeterminate()
    {
        var evaluation = Evaluate(
            AgentProvider.Codex,
            1000,
            Done(AgentProvider.Codex, Codex(1, 1)),
            Done(AgentProvider.ClaudeCode, null),
            (AttemptStatus.Running, AgentProvider.ClaudeCode, null));

        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, evaluation.State);
    }

    [Fact]
    public void An_unrepresentable_sum_is_reported_as_overflow_and_indeterminate_never_wrapped_or_saturated_into_a_verdict()
    {
        var accumulator = new AgentTokenStopAccumulator();
        accumulator.AddKnown(AgentProvider.Codex, long.MaxValue - 1);
        accumulator.AddKnown(AgentProvider.Codex, 2);

        var evaluation = accumulator.ToEvaluation(AgentProvider.Codex, 1000);

        Assert.True(evaluation.CountOverflowed);
        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, evaluation.State);
        Assert.True(evaluation.BlocksClaim);
        Assert.Equal(long.MaxValue, evaluation.KnownTokenCount);
    }

    [Fact]
    public void Overflow_of_one_provider_does_not_affect_the_other_provider()
    {
        var accumulator = new AgentTokenStopAccumulator();
        accumulator.AddKnown(AgentProvider.Codex, long.MaxValue);
        accumulator.AddKnown(AgentProvider.Codex, 1);
        accumulator.Add(AttemptStatus.Completed, AgentProvider.ClaudeCode, Claude(1, 1, 1, 1));

        var claude = accumulator.ToEvaluation(AgentProvider.ClaudeCode, 100);

        Assert.False(claude.CountOverflowed);
        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, claude.State);
    }

    [Fact]
    public void The_maximum_per_attempt_count_is_representable_without_overflow()
    {
        var evaluation = Evaluate(
            AgentProvider.ClaudeCode,
            4L * int.MaxValue,
            Done(AgentProvider.ClaudeCode, Claude(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)));

        Assert.False(evaluation.CountOverflowed);
        Assert.Equal(4L * int.MaxValue, evaluation.KnownTokenCount);
        Assert.Equal(AgentTokenStopState.ThresholdReached, evaluation.State);
    }

    [Fact]
    public void Blocking_states_map_to_distinct_safe_errors_and_permitting_states_to_none()
    {
        static AgentTokenStopEvaluation With(AgentTokenStopState state) =>
            new(AgentProvider.Codex, 1, state, 0, 0, 0, 0, 0, false);

        var reached = AgentTokenStopGate.ToError(With(AgentTokenStopState.ThresholdReached));
        var indeterminate = AgentTokenStopGate.ToError(With(AgentTokenStopState.EvidenceIndeterminate));

        Assert.Equal(AgentTokenStopGate.ReachedCode, reached!.Code);
        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, indeterminate!.Code);
        Assert.NotEqual(reached.Code, indeterminate.Code);
        Assert.All(new[] { reached.Description, indeterminate.Description }, message => Assert.DoesNotContain(message, char.IsDigit));
        Assert.Null(AgentTokenStopGate.ToError(With(AgentTokenStopState.NotConfigured)));
        Assert.Null(AgentTokenStopGate.ToError(With(AgentTokenStopState.NoDispatchedHistory)));
        Assert.Null(AgentTokenStopGate.ToError(With(AgentTokenStopState.BelowThresholdComplete)));
    }
}
