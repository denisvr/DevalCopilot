using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// A run-level aggregate of provider-reported token usage across every dispatched Agent attempt.
/// The sums cover only terminal attempts with known usage and are never nulled merely because the
/// aggregate is partial; <see cref="Completeness"/> is what prevents a partial sum from being read as
/// the run's total. All four counts include only dispatched Agent attempts — an undispatched attempt
/// never invoked a provider, so it is neither known, pending, nor terminal-unknown usage. The cache
/// sums are null when no known attempt reported that breakdown, and otherwise cover only the known
/// attempts that did.
/// <see cref="AttemptsWithUnknownUsage"/> preserves its original, broader meaning — every dispatched
/// attempt without known usage, regardless of why — and always equals the sum of
/// <see cref="PendingAttemptCount"/> (still running; not yet trusted, not a failure) and
/// <see cref="TerminalAttemptsWithUnknownUsage"/> (concluded without a trusted usage contract; a
/// genuine gap). Splitting the two lets a caller distinguish "still in flight" from "we tried and it
/// is missing or untrusted" without breaking any existing reader of the original count.
/// </summary>
public sealed record RunCockpitTokenUsageSummary(
    RunTokenUsageCompleteness Completeness,
    int AttemptsWithKnownUsage,
    int AttemptsWithUnknownUsage,
    int PendingAttemptCount,
    int TerminalAttemptsWithUnknownUsage,
    long InputTokens,
    long OutputTokens,
    long? CacheCreationInputTokens,
    long? CacheReadInputTokens)
{
    /// <summary>Aggregates one entry per dispatched Agent attempt: its terminal status (a still
    /// <see cref="AttemptStatus.Running"/> attempt is pending, and its evidence is never trusted
    /// regardless of what is persisted), and its known usage evidence, or <see langword="null"/> when
    /// that attempt's usage is absent or unknown.</summary>
    public static RunCockpitTokenUsageSummary FromDispatchedAttempts(
        IEnumerable<(AttemptStatus Status, AgentTokenUsageEvidence? Usage)> dispatchedAttempts)
    {
        ArgumentNullException.ThrowIfNull(dispatchedAttempts);
        var accumulator = new RunCockpitTokenUsageAccumulator();
        foreach (var (status, usage) in dispatchedAttempts)
        {
            accumulator.Add(status, usage);
        }

        return accumulator.ToSummary();
    }
}
