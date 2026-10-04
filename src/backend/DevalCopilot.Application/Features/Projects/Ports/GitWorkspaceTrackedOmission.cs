namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Why a tracked file has no attested before/after text for new Agent delivery. A reason is a fixed classification,
/// never repository text, an exception message, or a filesystem path.</summary>
public enum GitWorkspaceTrackedOmission
{
    /// <summary>The path is a root instruction file (<c>AGENTS.md</c> or <c>CLAUDE.md</c>); only the controlled instruction
    /// section delivers its text, so it is never even read for tracked evidence.</summary>
    ReservedInstructionFile,

    /// <summary>The porcelain state (a type change, an intent-to-add, a path that is also untracked, or a state with nothing to
    /// compare against HEAD) is not one whose comparison this host can state.</summary>
    UnsupportedStatus,

    /// <summary>The path is unmerged.</summary>
    Unmerged,

    /// <summary>The captured HEAD records the path with a mode other than a regular file.</summary>
    UnsupportedMode,

    /// <summary>The captured HEAD records the path as a symbolic link.</summary>
    SymbolicLink,

    /// <summary>The captured HEAD records the path as a submodule.</summary>
    Submodule,

    /// <summary>The current path is a directory, a device, or otherwise not a regular file.</summary>
    NotRegularFile,

    /// <summary>The opened file could not be proven to be exactly this path physically inside the approved worktree and a plain
    /// single-name regular file, or a deletion could not be proven under an owned parent, or the host has no such proof.</summary>
    ContainmentUnproven,

    /// <summary>The file could not be opened or read (an access failure is never evidence of absence).</summary>
    Unreadable,

    /// <summary>Either side is larger than the per-source byte bound.</summary>
    TooLarge,

    /// <summary>Either side has more lines than the per-source line bound.</summary>
    TooManyLines,

    /// <summary>Either side contains a NUL byte and is treated as binary.</summary>
    Binary,

    /// <summary>The current bytes are not valid UTF-8.</summary>
    InvalidUtf8,

    /// <summary>The captured HEAD has no usable local blob for the path where the status requires one.</summary>
    BaselineUnavailable,

    /// <summary>The baseline bytes read from Git do not match the object's recorded size and identity, so they are not proven raw
    /// blob bytes (a corrupt or truncated object, content a decoded stream cannot reproduce, or invalid UTF-8).</summary>
    BaselineUnverified,

    /// <summary>The path cannot be encoded safely in a host-built patch header or passed as a literal Git argument.</summary>
    UnencodablePath,

    /// <summary>The per-observation retained-source budget was already spent by earlier files.</summary>
    AggregateLimit,

    /// <summary>The proven before and after bytes are identical, so the host comparison has nothing to state.</summary>
    NoContentDifference,
}
