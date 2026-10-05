namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The advisory outcome of one explicit Codex account-usage warning check (ADR-0026). It is a fact about one read of the
/// host's Codex account, never eligibility, readiness, remaining capacity or usage attributable to the run.</summary>
public enum CodexAccountUsageWarningCheckState
{
    /// <summary>The run has no saved warning; nothing was observed.</summary>
    NotConfigured = 1,

    /// <summary>The run's stored warning is not a valid setting; nothing was observed.</summary>
    SettingInvalid = 2,

    /// <summary>No valid, current, applicable observation exists: the evidence was missing, invalid, partial, expired or superseded.
    /// Never read as below the threshold.</summary>
    Unavailable = 3,

    /// <summary>Every reported window is below the saved threshold and the provider reported no reached limit.</summary>
    Below = 4,

    /// <summary>A reported window is at or above the saved threshold, or the provider reported a reached limit.</summary>
    Reached = 5,
}
