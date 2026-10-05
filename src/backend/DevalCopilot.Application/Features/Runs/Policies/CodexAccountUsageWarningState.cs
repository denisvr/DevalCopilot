namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>What the run's saved advisory Codex account-usage warning is, as the cockpit states it.</summary>
public enum CodexAccountUsageWarningState
{
    /// <summary>No warning is saved (every historical run, and a cleared one).</summary>
    NotConfigured = 1,

    /// <summary>A valid threshold is saved.</summary>
    Configured = 2,

    /// <summary>The stored value is not a valid setting, so no number is shown; setting or clearing repairs it.</summary>
    Unknown = 3,
}
