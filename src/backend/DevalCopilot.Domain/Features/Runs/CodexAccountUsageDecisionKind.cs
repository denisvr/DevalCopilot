namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The two terminal pre-dispatch decisions of the account-usage stop. There is no recorded "allowed" decision: a passed guard
/// leaves no snapshot, and a historical attempt is never guessed to have been below the stop.</summary>
public enum CodexAccountUsageDecisionKind
{
    /// <summary>A validated window reached the claimed threshold, or the provider reported a reached-limit state.</summary>
    Reached = 1,

    /// <summary>Whether the account stayed below the threshold could not be established (no valid, fresh observation, or an
    /// unusable stored threshold), so the configured operation was refused.</summary>
    Unavailable = 2,
}
