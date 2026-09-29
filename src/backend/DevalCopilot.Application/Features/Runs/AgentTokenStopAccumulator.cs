using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// One-pass, constant-memory accumulation of dispatched Agent attempts into per-provider stop
/// evaluations. Deliberately independent of the advisory warning's state: it shares only the count
/// formula. A still-running attempt is only ever pending; a concluded attempt contributes its
/// provider's formula exactly once or counts as insufficient; an attempt with no known provider is a
/// gap for both providers. Arithmetic never wraps: an unrepresentable sum is reported as overflow.
/// </summary>
internal sealed class AgentTokenStopAccumulator
{
    private sealed class Bucket
    {
        public long Known;
        public bool Overflowed;
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
            _unattributed++;
            return;
        }

        if (status == AttemptStatus.Running)
        {
            bucket.Pending++;
            return;
        }

        var count = AgentTokenActivityFormula.Count(provider!.Value, usage);
        if (count is null)
        {
            bucket.Insufficient++;
            return;
        }

        bucket.Counted++;
        AddKnown(bucket, count.Value);
    }

    /// <summary>Adds an already-computed contribution to a provider's known count; the seam that
    /// makes the otherwise unreachable overflow path directly testable.</summary>
    internal void AddKnown(AgentProvider provider, long count) =>
        AddKnown(provider == AgentProvider.Codex ? _codex : _claude, count);

    public AgentTokenStopEvaluation ToEvaluation(AgentProvider provider, long? threshold)
    {
        var bucket = provider == AgentProvider.Codex ? _codex : _claude;
        var dispatched = bucket.Counted + bucket.Pending + bucket.Insufficient;
        var hasGap = bucket.Pending > 0 || bucket.Insufficient > 0 || _unattributed > 0;

        var state = threshold is not { } configured
            ? AgentTokenStopState.NotConfigured
            : bucket.Overflowed
                ? AgentTokenStopState.EvidenceIndeterminate
                : bucket.Known >= configured
                    ? AgentTokenStopState.ThresholdReached
                    : dispatched == 0 && _unattributed == 0
                        ? AgentTokenStopState.NoDispatchedHistory
                        : hasGap
                            ? AgentTokenStopState.EvidenceIndeterminate
                            : AgentTokenStopState.BelowThresholdComplete;

        return new AgentTokenStopEvaluation(
            provider,
            threshold,
            state,
            bucket.Overflowed ? long.MaxValue : bucket.Known,
            bucket.Counted,
            bucket.Pending,
            bucket.Insufficient,
            _unattributed,
            bucket.Overflowed);
    }

    private static void AddKnown(Bucket bucket, long count)
    {
        if (bucket.Overflowed)
        {
            return;
        }

        try
        {
            bucket.Known = checked(bucket.Known + count);
        }
        catch (OverflowException)
        {
            bucket.Overflowed = true;
        }
    }
}
