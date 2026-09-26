using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RunCockpitProviderTokenUsageProjectionTests
{
    private static readonly AgentTokenUsageEvidence CodexUsage =
        AgentTokenUsageEvidence.Create(1000, 200, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    private static readonly AgentTokenUsageEvidence ClaudeUsage =
        AgentTokenUsageEvidence.Create(500, 50, 5, 60, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

    private static (AttemptStatus Status, AgentProvider? Provider, AgentTokenUsageEvidence? Usage) Terminal(
        AgentProvider? provider, AgentTokenUsageEvidence? usage = null) => (AttemptStatus.Completed, provider, usage);

    private static (AttemptStatus Status, AgentProvider? Provider, AgentTokenUsageEvidence? Usage) Pending(
        AgentProvider? provider, AgentTokenUsageEvidence? usage = null) => (AttemptStatus.Running, provider, usage);

    [Fact]
    public void No_dispatched_attempts_still_returns_all_three_buckets_reporting_nothing_to_sum()
    {
        var entries = RunCockpitProviderTokenUsageProjection.FromDispatchedAttempts([]);

        Assert.Equal(3, entries.Count);
        Assert.Equal(
            [
                RunCockpitProviderTokenUsageAttribution.Codex,
                RunCockpitProviderTokenUsageAttribution.ClaudeCode,
                RunCockpitProviderTokenUsageAttribution.Unattributed,
            ],
            entries.Select(entry => entry.Attribution));
        Assert.All(entries, entry => Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, entry.Summary.Completeness));
    }

    [Fact]
    public void Codex_and_ClaudeCode_attempts_are_summed_in_separate_buckets_without_cross_contamination()
    {
        var entries = RunCockpitProviderTokenUsageProjection.FromDispatchedAttempts(
            [
                Terminal(AgentProvider.Codex, CodexUsage),
                Terminal(AgentProvider.Codex, CodexUsage),
                Terminal(AgentProvider.ClaudeCode, ClaudeUsage),
            ]);

        var byAttribution = entries.ToDictionary(entry => entry.Attribution, entry => entry.Summary);

        var codex = byAttribution[RunCockpitProviderTokenUsageAttribution.Codex];
        Assert.Equal(RunTokenUsageCompleteness.Complete, codex.Completeness);
        Assert.Equal(2, codex.AttemptsWithKnownUsage);
        Assert.Equal(2000, codex.InputTokens);
        Assert.Equal(400, codex.OutputTokens);

        var claude = byAttribution[RunCockpitProviderTokenUsageAttribution.ClaudeCode];
        Assert.Equal(RunTokenUsageCompleteness.Complete, claude.Completeness);
        Assert.Equal(1, claude.AttemptsWithKnownUsage);
        Assert.Equal(500, claude.InputTokens);
        Assert.Equal(50, claude.OutputTokens);
        Assert.Equal(5, claude.CacheCreationInputTokens);
        Assert.Equal(60, claude.CacheReadInputTokens);

        var unattributed = byAttribution[RunCockpitProviderTokenUsageAttribution.Unattributed];
        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, unattributed.Completeness);
    }

    [Fact]
    public void A_null_provider_falls_closed_to_the_unattributed_bucket_rather_than_being_dropped()
    {
        var entries = RunCockpitProviderTokenUsageProjection.FromDispatchedAttempts([Terminal(provider: null)]);

        var unattributed = entries.Single(entry => entry.Attribution == RunCockpitProviderTokenUsageAttribution.Unattributed);
        Assert.Equal(RunTokenUsageCompleteness.Partial, unattributed.Summary.Completeness);
        Assert.Equal(1, unattributed.Summary.TerminalAttemptsWithUnknownUsage);

        var codex = entries.Single(entry => entry.Attribution == RunCockpitProviderTokenUsageAttribution.Codex);
        var claude = entries.Single(entry => entry.Attribution == RunCockpitProviderTokenUsageAttribution.ClaudeCode);
        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, codex.Summary.Completeness);
        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, claude.Summary.Completeness);
    }

    [Fact]
    public void An_undefined_provider_value_also_falls_closed_to_unattributed()
    {
        var entries = RunCockpitProviderTokenUsageProjection.FromDispatchedAttempts([Terminal((AgentProvider)999)]);

        var unattributed = entries.Single(entry => entry.Attribution == RunCockpitProviderTokenUsageAttribution.Unattributed);
        Assert.Equal(1, unattributed.Summary.TerminalAttemptsWithUnknownUsage);
    }

    [Fact]
    public void A_running_attempt_in_one_bucket_never_counts_as_known_and_never_leaks_into_another_buckets_pending_count()
    {
        var entries = RunCockpitProviderTokenUsageProjection.FromDispatchedAttempts(
            [Pending(AgentProvider.Codex, CodexUsage), Terminal(AgentProvider.ClaudeCode, ClaudeUsage)]);

        var byAttribution = entries.ToDictionary(entry => entry.Attribution, entry => entry.Summary);

        var codex = byAttribution[RunCockpitProviderTokenUsageAttribution.Codex];
        Assert.Equal(RunTokenUsageCompleteness.PendingEvidence, codex.Completeness);
        Assert.Equal(0, codex.AttemptsWithKnownUsage);
        Assert.Equal(1, codex.PendingAttemptCount);

        var claude = byAttribution[RunCockpitProviderTokenUsageAttribution.ClaudeCode];
        Assert.Equal(RunTokenUsageCompleteness.Complete, claude.Completeness);
        Assert.Equal(0, claude.PendingAttemptCount);
    }
}
