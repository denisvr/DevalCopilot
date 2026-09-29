namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Why an untracked file has no text preview. A reason is a fixed classification, never
/// repository text, an exception message, or a filesystem path.</summary>
public enum GitWorkspaceUntrackedOmission
{
    /// <summary>A directory, nested repository, device, or other entry that is not a regular file.</summary>
    NotRegularFile,

    /// <summary>The opened file could not be proven to be exactly this path physically inside the
    /// approved worktree (a link or junction, a path form that cannot be proven, or a host without
    /// a physical-containment proof).</summary>
    ContainmentUnproven,

    /// <summary>The path no longer exists at read time.</summary>
    Missing,

    /// <summary>The file could not be opened or read.</summary>
    Unreadable,

    /// <summary>The opened bytes do not hash to the raw-content identity captured for the fingerprint.</summary>
    ContentIdentityMismatch,

    /// <summary>The file is larger than the bound within which its identity is verified.</summary>
    TooLarge,

    /// <summary>The bytes contain a NUL byte and are treated as binary.</summary>
    Binary,

    /// <summary>The bytes are not valid UTF-8.</summary>
    InvalidUtf8,

    /// <summary>The capture's aggregate preview budget was already spent by earlier files.</summary>
    AggregateLimit,
}
