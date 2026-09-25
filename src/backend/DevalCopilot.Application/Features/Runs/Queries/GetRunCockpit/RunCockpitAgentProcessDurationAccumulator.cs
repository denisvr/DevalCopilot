using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>One-pass, constant-memory aggregation over dispatched Agent attempts' own host-measured
/// process-duration evidence. Mirrors <see cref="RunCockpitTokenUsageAccumulator"/>'s shape: a
/// terminal attempt with missing or malformed evidence contributes only to the malformed count,
/// never to a partial sum, and a still-running attempt contributes only to the pending count.</summary>
internal sealed class RunCockpitAgentProcessDurationAccumulator
{
    private int _dispatched;
    private int _pending;
    private int _valid;
    private int _malformed;
    private long _totalTicks;
    private bool _overflowed;

    public void Add(AttemptStatus status, AgentProcessExecutionEvidence? evidence)
    {
        _dispatched = checked(_dispatched + 1);

        if (status == AttemptStatus.Running)
        {
            _pending = checked(_pending + 1);
            return;
        }

        if (evidence is null)
        {
            _malformed = checked(_malformed + 1);
            return;
        }

        _valid = checked(_valid + 1);
        if (_overflowed)
        {
            return;
        }

        try
        {
            _totalTicks = checked(_totalTicks + evidence.Duration.Ticks);
        }
        catch (OverflowException)
        {
            // Every individual duration summed so far is real, valid, terminal evidence — only their
            // running SUM is unrepresentable as a TimeSpan. That is a distinct, honest state
            // (UnrepresentableTotal, see ToSummary below) rather than a reason to discredit the
            // individual measurements as malformed.
            _overflowed = true;
        }
    }

    public RunCockpitAgentProcessDurationSummary ToSummary()
    {
        if (_dispatched == 0)
        {
            return RunCockpitAgentProcessDurationSummary.NoDispatchedAttempts();
        }

        // Malformed evidence always dominates the classification, exactly as before: a run with any
        // malformed terminal attempt is never described as UnrepresentableTotal even if the valid
        // subset's own sum also happens to overflow, because the malformed evidence is the more
        // important fact to surface first.
        if (_malformed > 0 && _valid == 0)
        {
            return RunCockpitAgentProcessDurationSummary.MalformedEvidence(_dispatched, _pending, _valid, _malformed);
        }

        if (_malformed > 0)
        {
            return RunCockpitAgentProcessDurationSummary.PartialEvidence(_dispatched, _pending, _valid, _malformed);
        }

        if (_overflowed)
        {
            return RunCockpitAgentProcessDurationSummary.UnrepresentableTotal(_dispatched, _pending, _valid);
        }

        if (_pending > 0)
        {
            return RunCockpitAgentProcessDurationSummary.PendingEvidence(_dispatched, _pending, _valid);
        }

        return RunCockpitAgentProcessDurationSummary.Complete(_dispatched, _valid, TimeSpan.FromTicks(_totalTicks));
    }
}
