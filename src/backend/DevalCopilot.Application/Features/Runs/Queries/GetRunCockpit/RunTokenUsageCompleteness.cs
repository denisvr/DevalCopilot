namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Whether a run's summed provider-reported token usage covers every dispatched Agent attempt.
/// Only <see cref="Complete"/> may ever be presented as the run's total; a <see cref="Partial"/> or
/// <see cref="PendingEvidence"/> sum is real but covers only the attempts with known usage, which is
/// the ordinary, truthful state of any run that has not yet finished every dispatched attempt with a
/// proven usage contract. A still-<c>Running</c> dispatched attempt is never counted as known usage,
/// even when its persisted row unexpectedly already carries token fields: only a terminal attempt's
/// evidence is trusted, so <see cref="Complete"/> requires every dispatched attempt to have both
/// concluded and reported known usage.
/// </summary>
public enum RunTokenUsageCompleteness
{
    /// <summary>No Agent attempt in the run has been dispatched yet, so there is nothing to sum.</summary>
    NoDispatchedAttempts = 0,

    /// <summary>Every dispatched Agent attempt in the run is terminal and has known token-usage
    /// evidence.</summary>
    Complete = 1,

    /// <summary>At least one terminal dispatched Agent attempt lacks known token-usage evidence —
    /// with or without other attempts still running. This is a genuine gap: a terminal attempt
    /// concluded without a trusted usage contract, not merely an attempt still in flight.</summary>
    Partial = 2,

    /// <summary>At least one dispatched Agent attempt is still running (no terminal result yet), and
    /// every terminal attempt observed so far has known token-usage evidence. Distinct from
    /// <see cref="Partial"/>: nothing has failed to report usage, some attempts simply have not
    /// concluded yet.</summary>
    PendingEvidence = 3,
}
