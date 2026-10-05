namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>What a saved or snapshotted Codex account-usage stop setting says.</summary>
public enum CodexAccountUsageStopState
{
    /// <summary>No stop is saved or was snapshotted.</summary>
    NotConfigured = 1,

    /// <summary>A valid threshold is saved or was snapshotted.</summary>
    Configured = 2,

    /// <summary>The stored value is not a valid setting, so no number is shown or enforced.</summary>
    Unknown = 3,
}
