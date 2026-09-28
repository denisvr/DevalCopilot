namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// The advisory state of one provider's run-scoped token-activity warning. Advisory only: it is
/// derived from locally recorded, provider-reported evidence and never reflects an account
/// allowance, a cost, or any claim/dispatch eligibility.
/// </summary>
public enum RunCockpitTokenWarningState
{
    /// <summary>No threshold is configured for this provider on this run. Neutral: no warning, no
    /// all-clear.</summary>
    NotConfigured = 0,

    /// <summary>The provider's known token-activity count is at or above the threshold (including
    /// exact equality). Valid even when the known count is only a lower bound because other
    /// evidence is missing.</summary>
    ThresholdReached = 1,

    /// <summary>A threshold is configured and no dispatched Agent attempt has been recorded for
    /// this provider, so there is no evidence either way. Not a known zero.</summary>
    NoEvidence = 2,

    /// <summary>The known count is below the threshold and every dispatched attempt (of this
    /// provider, and any unattributed one) contributed sufficient evidence: a complete
    /// below-threshold result. Still only a statement about locally recorded activity.</summary>
    BelowThresholdComplete = 3,

    /// <summary>The known count is below the threshold but at least one dispatched attempt is
    /// still running, lacks sufficient usage evidence, or could not be attributed to a provider.
    /// The true count may be higher: explicitly not an all-clear.</summary>
    Indeterminate = 4,
}
