namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Whether a run's summed provider-reported token usage covers every dispatched Agent attempt.
/// Only <see cref="Complete"/> may ever be presented as the run's total; a <see cref="Partial"/>
/// sum is real but covers only the attempts with known usage, which is the ordinary, truthful state
/// of any run that dispatched an attempt to a provider without a proven usage contract.
/// </summary>
public enum RunTokenUsageCompleteness
{
    /// <summary>No Agent attempt in the run has been dispatched yet, so there is nothing to sum.</summary>
    NoDispatchedAttempts = 0,

    /// <summary>Every dispatched Agent attempt in the run has known token-usage evidence.</summary>
    Complete = 1,

    /// <summary>At least one dispatched Agent attempt lacks known token-usage evidence.</summary>
    Partial = 2,
}
