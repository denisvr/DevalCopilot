namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;

/// <summary>What a claimed Codex attempt's own immutable snapshot says the dispatch guard must do.</summary>
public enum CodexAccountUsageStopPlanState
{
    /// <summary>The attempt claimed no stop (it was claimed with the stop disabled, or before the setting existed): it dispatches exactly
    /// as before and no observation is made.</summary>
    NotConfigured = 1,

    /// <summary>The attempt claimed a valid threshold: a fresh guard observation is required before dispatch.</summary>
    Threshold = 2,

    /// <summary>The attempt's stored threshold snapshot is not a valid setting: no number is guessed and no observation is made; the
    /// attempt is resolved without dispatch.</summary>
    Invalid = 3,
}
