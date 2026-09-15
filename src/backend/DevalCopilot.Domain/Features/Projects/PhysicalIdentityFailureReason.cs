namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// The closed, safe set of reasons a project's physical identity could not be resolved — never
/// a raw Win32 error code, exception message, native error text, or filesystem path. Persisted
/// verbatim and rendered through a fixed display string per value; nothing else about the
/// underlying failure ever crosses this boundary. See ADR-0008.
/// </summary>
public enum PhysicalIdentityFailureReason
{
    /// <summary>No failure. Only valid when <see cref="Project.PhysicalIdentityStatus"/> is not
    /// <see cref="PhysicalIdentityStatus.Unavailable"/>.</summary>
    None = 0,

    /// <summary>The containing volume is not NTFS or ReFS, so a stable 128-bit file ID cannot
    /// be obtained.</summary>
    UnsupportedFilesystem = 1,

    /// <summary>The path could not be opened at all (access denied, missing, or a sharing
    /// violation) — possibly transient.</summary>
    PathInaccessible = 2,
}
