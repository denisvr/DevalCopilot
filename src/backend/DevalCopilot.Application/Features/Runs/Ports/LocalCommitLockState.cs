namespace DevalCopilot.Application.Features.Runs.Ports;

public enum LocalCommitLockState
{
    None,

    /// <summary>Any extant index lock. A pathname and matching bytes are never operation ownership, so a lock found after a
    /// restart is never adopted, promoted or removed; it keeps the operation and workspace under attention.</summary>
    Unknown,
}
