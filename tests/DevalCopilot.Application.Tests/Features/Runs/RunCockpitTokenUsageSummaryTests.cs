using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RunCockpitTokenUsageSummaryTests
{
    private static readonly AgentTokenUsageEvidence First = AgentTokenUsageEvidence.Create(1000, 200, 30, 400, "claude-cli-usage-v1");
    private static readonly AgentTokenUsageEvidence Second = AgentTokenUsageEvidence.Create(500, 50, 5, 60, "claude-cli-usage-v1");

    private static (AttemptStatus Status, AgentTokenUsageEvidence? Usage) Terminal(AgentTokenUsageEvidence? usage = null) =>
        (AttemptStatus.Completed, usage);

    private static (AttemptStatus Status, AgentTokenUsageEvidence? Usage) Pending(AgentTokenUsageEvidence? usage = null) =>
        (AttemptStatus.Running, usage);

    [Fact]
    public void No_dispatched_attempts_reports_nothing_to_sum()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([]);

        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, summary.Completeness);
        Assert.Equal(0, summary.AttemptsWithKnownUsage);
        Assert.Equal(0, summary.AttemptsWithUnknownUsage);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(0, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(0, summary.InputTokens);
        Assert.Equal(0, summary.OutputTokens);
        Assert.Null(summary.CacheCreationInputTokens);
        Assert.Null(summary.CacheReadInputTokens);
    }

    [Fact]
    public void Every_dispatched_attempt_terminal_and_known_is_complete()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(First), Terminal(Second)]);

        Assert.Equal(RunTokenUsageCompleteness.Complete, summary.Completeness);
        Assert.Equal(2, summary.AttemptsWithKnownUsage);
        Assert.Equal(0, summary.AttemptsWithUnknownUsage);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(0, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(1500, summary.InputTokens);
        Assert.Equal(250, summary.OutputTokens);
        Assert.Equal(35, summary.CacheCreationInputTokens);
        Assert.Equal(460, summary.CacheReadInputTokens);
    }

    // A genuine terminal gap — a concluded attempt with no trusted usage contract — is Partial, and
    // its sum is still real information, never nulled, but flagged as covering only the known subset.
    [Fact]
    public void Any_terminal_unknown_dispatched_attempt_is_partial_and_sums_only_the_known_attempts()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts(
            [Terminal(First), Terminal(), Terminal(Second), Terminal()]);

        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(2, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(2, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(1500, summary.InputTokens);
        Assert.Equal(250, summary.OutputTokens);
    }

    [Fact]
    public void Only_terminal_unknown_dispatched_attempts_is_partial_with_zero_sums()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(), Terminal()]);

        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(0, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(2, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(0, summary.InputTokens);
    }

    // All dispatched attempts still running, none terminal — nothing has failed to report usage,
    // some attempts simply have not concluded yet. Distinct from a genuine terminal gap.
    [Fact]
    public void All_attempts_still_pending_reports_pending_evidence()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Pending(), Pending()]);

        Assert.Equal(RunTokenUsageCompleteness.PendingEvidence, summary.Completeness);
        Assert.Equal(0, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(2, summary.PendingAttemptCount);
        Assert.Equal(0, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(0, summary.InputTokens);
    }

    // All dispatched attempts are terminal, all have valid usage — the pure terminal-unknown-only
    // scenario is covered above; this proves a run whose every attempt already concluded successfully
    // reports Complete rather than any partial state, matching the required "all-terminal-known" case.
    [Fact]
    public void All_terminal_known_reports_complete_even_with_a_single_attempt()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(First)]);

        Assert.Equal(RunTokenUsageCompleteness.Complete, summary.Completeness);
        Assert.Equal(1, summary.AttemptsWithKnownUsage);
        Assert.Equal(0, summary.AttemptsWithUnknownUsage);
    }

    // A genuine three-way mix: one known terminal attempt, one terminal attempt with no usage
    // evidence, and one attempt still running.
    [Fact]
    public void Mixed_known_pending_and_terminal_unknown_reports_partial_with_the_full_breakdown()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(First), Terminal(), Pending()]);

        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(1, summary.AttemptsWithKnownUsage);
        Assert.Equal(2, summary.AttemptsWithUnknownUsage);
        Assert.Equal(1, summary.PendingAttemptCount);
        Assert.Equal(1, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(1000, summary.InputTokens);
        Assert.Equal(200, summary.OutputTokens);
    }

    // The core correctness rule this slice fixes: a Running attempt's usage is NEVER trusted, even
    // when its persisted row already carries seemingly-valid, well-formed token fields (e.g. from a
    // corrupted or prematurely-populated row). It must still be classified as pending, not known.
    [Fact]
    public void A_running_attempt_with_corrupted_seemingly_valid_usage_is_never_counted_as_known()
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Pending(First), Terminal(Second)]);

        Assert.Equal(RunTokenUsageCompleteness.PendingEvidence, summary.Completeness);
        Assert.Equal(1, summary.AttemptsWithKnownUsage);
        Assert.Equal(1, summary.AttemptsWithUnknownUsage);
        Assert.Equal(1, summary.PendingAttemptCount);
        Assert.Equal(0, summary.TerminalAttemptsWithUnknownUsage);
        // Only Second's tokens are summed; First's corrupted-but-present evidence is fully discarded.
        Assert.Equal(500, summary.InputTokens);
        Assert.Equal(50, summary.OutputTokens);
    }

    [Fact]
    public void Cache_sums_cover_only_known_attempts_that_reported_a_cache_breakdown()
    {
        var withoutCache = AgentTokenUsageEvidence.Create(10, 1, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

        var onlyWithoutCache = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(withoutCache)]);
        var mixed = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(withoutCache), Terminal(Second)]);

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

        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts([Terminal(large), Terminal(large)]);

        Assert.Equal(2L * int.MaxValue, summary.InputTokens);
        Assert.Equal(2L * int.MaxValue, summary.OutputTokens);
    }

    [Fact]
    public void Large_single_pass_sequence_is_aggregated_exactly_without_a_row_cap()
    {
        var enumerations = 0;
        IEnumerable<(AttemptStatus Status, AgentTokenUsageEvidence? Usage)> Stream()
        {
            enumerations++;
            for (var index = 0; index < 10_000; index++)
            {
                yield return index % 4 == 0 ? Terminal() : Terminal(First);
            }
        }

        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts(Stream());

        Assert.Equal(1, enumerations);
        Assert.Equal(RunTokenUsageCompleteness.Partial, summary.Completeness);
        Assert.Equal(7_500, summary.AttemptsWithKnownUsage);
        Assert.Equal(2_500, summary.AttemptsWithUnknownUsage);
        Assert.Equal(2_500, summary.TerminalAttemptsWithUnknownUsage);
        Assert.Equal(0, summary.PendingAttemptCount);
        Assert.Equal(7_500_000, summary.InputTokens);
        Assert.Equal(1_500_000, summary.OutputTokens);
        Assert.Equal(225_000, summary.CacheCreationInputTokens);
        Assert.Equal(3_000_000, summary.CacheReadInputTokens);
    }

    // The arithmetic relationship AttemptsWithUnknownUsage == PendingAttemptCount +
    // TerminalAttemptsWithUnknownUsage must hold across every completeness state — the split is
    // strictly additive over the original, broader "unknown" count, never a replacement for it.
    [Theory]
    [MemberData(nameof(CompletenessScenarios))]
    public void Unknown_count_always_equals_pending_plus_terminal_unknown(
        IEnumerable<(AttemptStatus Status, AgentTokenUsageEvidence? Usage)> attempts)
    {
        var summary = RunCockpitTokenUsageSummary.FromDispatchedAttempts(attempts);

        Assert.Equal(summary.AttemptsWithUnknownUsage, summary.PendingAttemptCount + summary.TerminalAttemptsWithUnknownUsage);
    }

    public static TheoryData<IEnumerable<(AttemptStatus Status, AgentTokenUsageEvidence? Usage)>> CompletenessScenarios()
    {
        var data = new TheoryData<IEnumerable<(AttemptStatus Status, AgentTokenUsageEvidence? Usage)>>();
        data.Add(Array.Empty<(AttemptStatus, AgentTokenUsageEvidence?)>());
        data.Add([Terminal(First), Terminal(Second)]);
        data.Add([Terminal(First), Terminal(), Terminal(Second), Terminal()]);
        data.Add([Pending(), Pending()]);
        data.Add([Terminal(First), Terminal(), Pending()]);
        return data;
    }
}
