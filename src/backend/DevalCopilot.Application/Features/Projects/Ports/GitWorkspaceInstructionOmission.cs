namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Why a root file has no text. A reason is a fixed classification, never repository text, an exception
/// message, or a filesystem path.</summary>
public enum GitWorkspaceInstructionOmission
{
    /// <summary>The file exists but is ignored by Git and not tracked, so no Git evidence vouches for it.</summary>
    Ignored,

    /// <summary>The tracked entry is marked assume-unchanged or skip-worktree, so Git status does not vouch for it.</summary>
    IndexFlag,

    /// <summary>The tracked entry is in an unmerged (conflicted) index state.</summary>
    Unmerged,

    /// <summary>A directory, device, or other entry that is not a regular file.</summary>
    NotRegularFile,

    /// <summary>The opened file could not be proven to be exactly this path physically inside the approved
    /// worktree (a link or junction, a path form that cannot be proven, or a host without a containment proof).</summary>
    ContainmentUnproven,

    /// <summary>The file could not be opened or read.</summary>
    Unreadable,

    /// <summary>The opened bytes do not hash to the raw-content identity Git reports for the same path.</summary>
    ContentIdentityMismatch,

    /// <summary>The file is larger than <see cref="GitWorkspaceInstructionContext.MaxSourceBytes"/>.</summary>
    TooLarge,

    /// <summary>The bytes contain a NUL byte and are treated as binary.</summary>
    Binary,

    /// <summary>The bytes are not valid UTF-8.</summary>
    InvalidUtf8,
}
