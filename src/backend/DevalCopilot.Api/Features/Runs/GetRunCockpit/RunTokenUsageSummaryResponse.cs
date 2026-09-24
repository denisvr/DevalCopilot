using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// Provider-reported token usage summed across the run's dispatched Agent attempts.
/// <c>Completeness</c> is <c>NoDispatchedAttempts</c>, <c>Complete</c>, or <c>Partial</c>; only a
/// <c>Complete</c> summary is the run's total. A <c>Partial</c> summary's sums cover only the
/// <c>AttemptsWithKnownUsage</c> attempts and must never be presented as the run's total. The cache
/// sums are null when no known attempt reported that breakdown. Never carries a schema version,
/// path, argument, environment value, output, session identifier, or credential.
/// </summary>
public sealed record RunTokenUsageSummaryResponse(
    string Completeness,
    int AttemptsWithKnownUsage,
    int AttemptsWithUnknownUsage,
    long InputTokens,
    long OutputTokens,
    long? CacheCreationInputTokens,
    long? CacheReadInputTokens)
{
    public static RunTokenUsageSummaryResponse FromSummary(RunCockpitTokenUsageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new RunTokenUsageSummaryResponse(
            summary.Completeness.ToString(),
            summary.AttemptsWithKnownUsage,
            summary.AttemptsWithUnknownUsage,
            summary.InputTokens,
            summary.OutputTokens,
            summary.CacheCreationInputTokens,
            summary.CacheReadInputTokens);
    }
}
