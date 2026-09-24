using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RunCockpitTokenUsageSummaryTests
{
    private static readonly AgentTokenUsageEvidence First = AgentTokenUsageEvidence.Create(1000, 200, 30, 400, "claude-cli-usage-v1");
    private static readonly AgentTokenUsageEvidence Second = AgentTokenUsageEvidence.Create(500, 50, 5, 60, "claude-cli-usage-v1");

    [Fact]
    public void No_dispatched_attempts_reports_nothing_to_sum()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([]);

        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, summary.Completeness);
        Assert.Equal(0, summary.AttemptsWithKnownUsage);
        Assert.Equal(0, summary.AttemptsWithUnknownUsage);
        Assert.Equal(0, summary.InputTokens);
        Assert.Equal(0, summary.OutputTokens);
        Assert.Null(summary.CacheCreationInputTokens);
        Assert.Null(summary.CacheReadInputTokens);
    }

    [Fact]
    public void Every_dispatched_attempt_known_is_complete()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([First, Second]);

        Assert.Equal(RunTokenUsageCompleteness.Complete, summary.Completeness);
        Assert.Equal(2, summary.AttemptsWithKnownUsage);
        Assert.Equal(0, summary.AttemptsWithUnknownUsage);
        Assert.Equal(1500, summary.InputTokens);
        Assert.Equal(250, summary.OutputTokens);
        Assert.Equal(35, summary.CacheCreationInputTokens);
        Assert.Equal(460, summary.CacheReadInputTokens);
    }

    // A partial sum is still real information and is never nulled, but completeness must flag it.
    [Fact]
    public void Any_unknown_dispatched_attempt_is_partial_and_sums_only_the_known_attempts()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([First, null, Second, null]);

        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(2, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(1500, summary.InputTokens);
        Assert.Equal(250, summary.OutputTokens);
    }

    [Fact]
    public void Only_unknown_dispatched_attempts_is_partial_with_zero_sums()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([null, null]);

        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(0, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(0, summary.InputTokens);
    }

    [Fact]
    public void Cache_sums_cover_only_known_attempts_that_reported_a_cache_breakdown()
    {
        var withoutCache = AgentTokenUsageEvidence.Create(10, 1, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

        var onlyWithoutCache = RunCockpitTokenUsageSummary.FromDispatchedAttempts([withoutCache]);
        var mixed = RunCockpitTokenUsageSummary.FromDispatchedAttempts([withoutCache, Second]);

        Assert.Null(onlyWithoutCache.CacheCreationInputTokens);
        Assert.Null(onlyWithoutCache.CacheReadInputTokens);
        Assert.Equal(5, mixed.CacheCreationInputTokens);
        Assert.Equal(60, mixed.CacheReadInputTokens);
        Assert.Equal(510, mixed.InputTokens);
    }

    [Fact]
    public void Sums_do_not_overflow_the_per_attempt_integer_range()
    {
        var large = AgentTokenUsageEvidence.Create(int.MaxValue, int.MaxValue, null, null, "claude-cli-usage-v1");

        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([large, large]);

        Assert.Equal(2L * int.MaxValue, summary.InputTokens);
        Assert.Equal(2L * int.MaxValue, summary.OutputTokens);
    }

    [Fact]
    public void Large_single_pass_sequence_is_aggregated_exactly_without_a_row_cap()
    {
        var enumerations = 0;
        IEnumerable<AgentTokenUsageEvidence?> Stream()
        {
            enumerations++;
            for (var index = 0; index < 10_000; index++)
            {
                yield return index % 4 == 0 ? null : First;
            }
        }

        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts(Stream());

        Assert.Equal(1, enumerations);
        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(7_500, summary.AttemptsWithKnownUsage);
        Assert.Equal(2_500, summary.AttemptsWithUnknownUsage);
        Assert.Equal(7_500_000, summary.InputTokens);
        Assert.Equal(1_500_000, summary.OutputTokens);
        Assert.Equal(225_000, summary.CacheCreationInputTokens);
        Assert.Equal(3_000_000, summary.CacheReadInputTokens);
    }
}
