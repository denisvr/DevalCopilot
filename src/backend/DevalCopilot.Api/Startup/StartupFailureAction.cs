namespace DevalCopilot.Api.Startup;

/// <summary>ADR-0072: the one fixed guidance a fatal startup failure carries. Neither value advises database edits, reset, deletion,
/// force-release or repeated blind restarts, and neither promises self-healing.</summary>
public enum StartupFailureAction
{
    /// <summary>Restore ordinary access to the application data and free space, then reopen once after the correction; a failure that
    /// persists needs reviewed repair.</summary>
    RestoreStorageAccess = 0,

    /// <summary>Preserve the data and worktrees, copy the safe diagnostic and request a reviewed repair.</summary>
    RequestReviewedRepair = 1,
}
