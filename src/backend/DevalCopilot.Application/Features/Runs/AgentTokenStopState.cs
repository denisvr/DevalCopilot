namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// One provider's token-activity stop state for a run, derived from locally recorded evidence only.
/// It is a retrospective claim guardrail, never an account allowance and never a statement that a
/// provider invocation is possible: <see cref="BelowThresholdComplete"/> and
/// <see cref="NoDispatchedHistory"/> mean only that this stop does not block a claim.
/// </summary>
public enum AgentTokenStopState
{
    /// <summary>No stop threshold is configured for this provider; claims are unaffected.</summary>
    NotConfigured = 0,

    /// <summary>A threshold is configured and the run has no dispatched Agent attempt at all, so
    /// the first claim is permitted. Not a measured zero.</summary>
    NoDispatchedHistory = 1,

    /// <summary>The known count is below the threshold and every dispatched attempt (of this
    /// provider, and any unattributed one) contributed sufficient evidence. The stop permits a
    /// claim; nothing here asserts the provider itself is eligible.</summary>
    BelowThresholdComplete = 2,

    /// <summary>The known count is at or above the threshold (including exact equality). Blocks a
    /// new claim even when other evidence is incomplete, since the count is only a lower bound then.</summary>
    ThresholdReached = 3,

    /// <summary>The known count is not at or above the threshold, but staying below it cannot be
    /// proved: relevant usage is pending, missing, malformed, unsupported, unattributed, or the sum
    /// is not representable. Blocks a new claim; never treated as zero or as complete.</summary>
    EvidenceIndeterminate = 4,
}
