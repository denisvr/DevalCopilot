using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// One-pass, constant-memory accumulation behind <see cref="RunCockpitTokenWarningProjection"/>.
/// A still-running attempt is only ever pending. A concluded attempt contributes its provider's
/// formula exactly once, or counts as insufficient evidence: Codex counts input + output (its cached
/// input is already inside input and is not added again); Claude Code counts input + cache creation
/// + cache read + output and, unlike the raw usage view, treats a row missing either cache count as
/// insufficient rather than substituting zero. An attempt with no known provider is a gap for both
/// providers and is never assigned to either.
/// </summary>
internal sealed class RunCockpitTokenWarningAccumulator
{
    private sealed class Bucket
    {
        public long Known;
        public int Counted;
        public int Pending;
        public int Insufficient;
    }

    private readonly Bucket _codex = new();
    private readonly Bucket _claude = new();
    private int _unattributed;

    public void Add(AttemptStatus status, AgentProvider? provider, AgentTokenUsageEvidence? usage)
    {
        var bucket = provider switch
        {
            AgentProvider.Codex => _codex,
            AgentProvider.ClaudeCode => _claude,
            _ => null,
        };

        if (bucket is null)
        {
            _unattributed = checked(_unattributed + 1);
            return;
        }

        if (status == AttemptStatus.Running)
        {
            bucket.Pending = checked(bucket.Pending + 1);
            return;
        }

        var count = Contribution(provider!.Value, usage);
        if (count is null)
        {
            bucket.Insufficient = checked(bucket.Insufficient + 1);
            return;
        }

        bucket.Counted = checked(bucket.Counted + 1);
        bucket.Known = checked(bucket.Known + count.Value);
    }

    public IReadOnlyList<RunCockpitTokenWarningEntry> ToEntries(long? codexThreshold, long? claudeThreshold) =>
    [
        ToEntry(AgentProvider.Codex, _codex, codexThreshold),
        ToEntry(AgentProvider.ClaudeCode, _claude, claudeThreshold),
    ];

    private RunCockpitTokenWarningEntry ToEntry(AgentProvider provider, Bucket bucket, long? threshold)
    {
        var dispatched = bucket.Counted + bucket.Pending + bucket.Insufficient;
        var hasGap = bucket.Pending > 0 || bucket.Insufficient > 0 || _unattributed > 0;

        var state = threshold is not { } configured
            ? RunCockpitTokenWarningState.NotConfigured
            : bucket.Known >= configured
                ? RunCockpitTokenWarningState.ThresholdReached
                : dispatched == 0 && _unattributed == 0
                    ? RunCockpitTokenWarningState.NoEvidence
                    : hasGap
                        ? RunCockpitTokenWarningState.Indeterminate
                        : RunCockpitTokenWarningState.BelowThresholdComplete;

        return new RunCockpitTokenWarningEntry(
            provider, threshold, state, bucket.Known, bucket.Counted, bucket.Pending, bucket.Insufficient, _unattributed);
    }

    private static long? Contribution(AgentProvider provider, AgentTokenUsageEvidence? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return provider switch
        {
            AgentProvider.Codex => (long)usage.InputTokens + usage.OutputTokens,
            AgentProvider.ClaudeCode when usage.CacheCreationInputTokens is { } creation && usage.CacheReadInputTokens is { } read =>
                (long)usage.InputTokens + creation + read + usage.OutputTokens,
            _ => null,
        };
    }
}
