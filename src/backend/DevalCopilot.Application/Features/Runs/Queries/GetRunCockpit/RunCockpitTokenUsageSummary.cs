using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// A run-level aggregate of provider-reported token usage across every dispatched Agent attempt.
/// The sums cover only attempts with known usage and are never nulled merely because the aggregate
/// is partial; <see cref="Completeness"/> is what prevents a partial sum from being read as the
/// run's total. Both counts include only dispatched Agent attempts — an undispatched attempt never
/// invoked a provider, so it is neither known nor unknown usage. The cache sums are null when no
/// known attempt reported that breakdown, and otherwise cover only the known attempts that did.
/// </summary>
public sealed record RunCockpitTokenUsageSummary(
    RunTokenUsageCompleteness Completeness,
    int AttemptsWithKnownUsage,
    int AttemptsWithUnknownUsage,
    long InputTokens,
    long OutputTokens,
    long? CacheCreationInputTokens,
    long? CacheReadInputTokens)
{
    /// <summary>Aggregates one entry per dispatched Agent attempt: its known evidence, or
    /// <see langword="null"/> when that attempt's usage is absent or unknown.</summary>
    public static RunCockpitTokenUsageSummary FromDispatchedAttempts(IEnumerable<AgentTokenUsageEvidence?> dispatchedAttemptUsage)
    {
        ArgumentNullException.ThrowIfNull(dispatchedAttemptUsage);
        var accumulator = new RunCockpitTokenUsageAccumulator();
        foreach (var usage in dispatchedAttemptUsage)
        {
            accumulator.Add(usage);
        }

        return accumulator.ToSummary();
    }
}
