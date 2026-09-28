using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RunCockpitTokenWarningProjectionTests
{
    private static AgentTokenUsageEvidence Codex(int input, int output) =>
        AgentTokenUsageEvidence.Create(input, output, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    private static AgentTokenUsageEvidence Claude(int input, int output, int? creation, int? read) =>
        AgentTokenUsageEvidence.Create(input, output, creation, read, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

    private static (AttemptStatus, AgentProvider?, AgentTokenUsageEvidence?) Done(AgentProvider? provider, AgentTokenUsageEvidence? usage) =>
        (AttemptStatus.Completed, provider, usage);

    private static (RunCockpitTokenWarningEntry Codex, RunCockpitTokenWarningEntry Claude) Project(
        long? codexThreshold, long? claudeThreshold, params (AttemptStatus, AgentProvider?, AgentTokenUsageEvidence?)[] attempts)
    {
        var entries = RunCockpitTokenWarningProjection.FromDispatchedAttempts(codexThreshold, claudeThreshold, attempts);
        Assert.Equal(2, entries.Count);
        Assert.Equal(AgentProvider.Codex, entries[0].Provider);
        Assert.Equal(AgentProvider.ClaudeCode, entries[1].Provider);
        return (entries[0], entries[1]);
    }

    [Fact]
    public void Without_thresholds_both_providers_are_neutral_but_still_report_their_known_counts()
    {
        var (codex, claude) = Project(null, null, Done(AgentProvider.Codex, Codex(1000, 200)), Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, 60)));

        Assert.Equal(RunCockpitTokenWarningState.NotConfigured, codex.State);
        Assert.Equal(RunCockpitTokenWarningState.NotConfigured, claude.State);
        Assert.Null(codex.ThresholdTokens);
        Assert.Equal(1200, codex.KnownTokenCount);
        Assert.Equal(615, claude.KnownTokenCount);
    }

    [Fact]
    public void Codex_counts_input_plus_output_only_and_never_adds_cached_input_again()
    {
        // Codex's proven schema cannot carry a cache breakdown at all, so its cached input is only
        // ever part of the reported input and is counted exactly once there.
        Assert.Throws<ArgumentException>(() => AgentTokenUsageEvidence.Create(
            1000, 200, 300, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion));

        var (codex, _) = Project(1200, null, Done(AgentProvider.Codex, Codex(1000, 200)));

        Assert.Equal(1200, codex.KnownTokenCount);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, codex.State);
    }

    [Fact]
    public void Claude_counts_input_cache_creation_cache_read_and_output_each_exactly_once()
    {
        var (_, claude) = Project(null, 615, Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, 60)));

        Assert.Equal(615, claude.KnownTokenCount);
        Assert.Equal(1, claude.CountedAttempts);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, claude.State);
    }

    [Fact]
    public void Exact_equality_warns_and_one_below_is_a_complete_below_threshold_result()
    {
        var attempts = new[] { Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, 60)) };

        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, Project(null, 615, attempts).Claude.State);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, Project(null, 614, attempts).Claude.State);
        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, Project(null, 616, attempts).Claude.State);
    }

    [Fact]
    public void A_genuine_known_zero_is_distinct_from_no_evidence()
    {
        var (_, knownZero) = Project(null, 10, Done(AgentProvider.ClaudeCode, Claude(0, 0, 0, 0)));
        var (_, noEvidence) = Project(null, 10);

        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, knownZero.State);
        Assert.Equal(0, knownZero.KnownTokenCount);
        Assert.Equal(1, knownZero.CountedAttempts);
        Assert.Equal(RunCockpitTokenWarningState.NoEvidence, noEvidence.State);
        Assert.Equal(0, noEvidence.CountedAttempts);
    }

    [Fact]
    public void A_claude_row_missing_either_cache_count_is_insufficient_and_never_counted_as_zero()
    {
        foreach (var usage in new[] { Claude(500, 50, null, 60), Claude(500, 50, 5, null), Claude(500, 50, null, null) })
        {
            var (_, claude) = Project(null, 100_000, Done(AgentProvider.ClaudeCode, usage));

            Assert.Equal(RunCockpitTokenWarningState.Indeterminate, claude.State);
            Assert.Equal(0, claude.KnownTokenCount);
            Assert.Equal(0, claude.CountedAttempts);
            Assert.Equal(1, claude.InsufficientEvidenceAttempts);
        }
    }

    [Fact]
    public void Below_threshold_with_missing_malformed_or_pending_evidence_is_indeterminate_never_an_all_clear()
    {
        var counted = Done(AgentProvider.Codex, Codex(10, 5));

        var missing = Project(1000, null, counted, Done(AgentProvider.Codex, null)).Codex;
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, missing.State);
        Assert.Equal(15, missing.KnownTokenCount);
        Assert.Equal(1, missing.InsufficientEvidenceAttempts);

        var pending = Project(1000, null, counted, (AttemptStatus.Running, AgentProvider.Codex, Codex(999_999, 1))).Codex;
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, pending.State);
        Assert.Equal(15, pending.KnownTokenCount);
        Assert.Equal(1, pending.PendingAttempts);
    }

    [Fact]
    public void Reaching_the_threshold_warns_even_when_other_evidence_is_missing()
    {
        var (codex, _) = Project(
            15, null, Done(AgentProvider.Codex, Codex(10, 5)), Done(AgentProvider.Codex, null), (AttemptStatus.Running, AgentProvider.Codex, null));

        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, codex.State);
        Assert.Equal(1, codex.InsufficientEvidenceAttempts);
        Assert.Equal(1, codex.PendingAttempts);
    }

    [Fact]
    public void An_unattributed_dispatched_attempt_is_never_assigned_and_blocks_an_all_clear_for_both_providers()
    {
        var (codex, claude) = Project(
            1000, 1000,
            Done(AgentProvider.Codex, Codex(10, 5)),
            Done(AgentProvider.ClaudeCode, Claude(1, 1, 1, 1)),
            Done(null, Codex(999_999, 999_999)),
            Done((AgentProvider)99, Codex(999_999, 999_999)));

        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, codex.State);
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, claude.State);
        Assert.Equal(15, codex.KnownTokenCount);
        Assert.Equal(4, claude.KnownTokenCount);
        Assert.Equal(2, codex.UnattributedAttempts);
        Assert.Equal(2, claude.UnattributedAttempts);
    }

    [Fact]
    public void Only_unattributed_evidence_is_indeterminate_rather_than_no_evidence()
    {
        var (codex, claude) = Project(10, 10, Done(null, null));

        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, codex.State);
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, claude.State);
    }

    [Fact]
    public void The_two_providers_warn_independently_and_are_never_combined()
    {
        var (codex, claude) = Project(1200, 10_000, Done(AgentProvider.Codex, Codex(1000, 200)), Done(AgentProvider.ClaudeCode, Claude(500, 50, 5, 60)));

        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, codex.State);
        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, claude.State);
        Assert.Equal(1200, codex.KnownTokenCount);
        Assert.Equal(615, claude.KnownTokenCount);
    }

    [Fact]
    public void Changing_a_threshold_re_evaluates_the_same_evidence()
    {
        var attempts = new[] { Done(AgentProvider.Codex, Codex(1000, 200)) };

        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, Project(5_000, null, attempts).Codex.State);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, Project(1_200, null, attempts).Codex.State);
        Assert.Equal(RunCockpitTokenWarningState.NotConfigured, Project(null, null, attempts).Codex.State);
    }

    [Fact]
    public void Sums_do_not_overflow_a_thirty_two_bit_integer()
    {
        var attempts = Enumerable.Range(0, 3)
            .Select(_ => Done(AgentProvider.Codex, Codex(int.MaxValue, int.MaxValue)))
            .ToArray();

        var (codex, _) = Project(long.MaxValue / 2, null, attempts);

        Assert.Equal(3L * 2 * int.MaxValue, codex.KnownTokenCount);
    }
}
