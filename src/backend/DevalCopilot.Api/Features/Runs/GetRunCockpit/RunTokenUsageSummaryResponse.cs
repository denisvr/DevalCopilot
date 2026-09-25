using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// Provider-reported token usage summed across the run's dispatched Agent attempts.
/// <c>Completeness</c> is <c>NoDispatchedAttempts</c>, <c>Complete</c>, <c>Partial</c>, or
/// <c>PendingEvidence</c>; only a <c>Complete</c> summary is the run's total. A <c>Partial</c> or
/// <c>PendingEvidence</c> summary's sums cover only the <c>AttemptsWithKnownUsage</c> attempts and
/// must never be presented as the run's total. <c>AttemptsWithUnknownUsage</c> keeps its original
/// meaning — every dispatched attempt without known usage — and always equals
/// <c>PendingAttemptCount</c> plus <c>TerminalAttemptsWithUnknownUsage</c>: a still-running attempt
/// (pending; not yet trusted, not a failure) is never counted as known usage even if its persisted
/// row already carries seemingly-valid token fields, and is kept distinct from a terminal attempt
/// that concluded without a trusted usage contract (a genuine gap). The cache sums are null when no
/// known attempt reported that breakdown. Never carries a schema version, path, argument,
/// environment value, output, session identifier, or credential.
/// </summary>
public sealed record RunTokenUsageSummaryResponse(
    string Completeness,
    int AttemptsWithKnownUsage,
    int AttemptsWithUnknownUsage,
    int PendingAttemptCount,
    int TerminalAttemptsWithUnknownUsage,
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
            summary.PendingAttemptCount,
            summary.TerminalAttemptsWithUnknownUsage,
            summary.InputTokens,
            summary.OutputTokens,
            summary.CacheCreationInputTokens,
            summary.CacheReadInputTokens);
    }
}
