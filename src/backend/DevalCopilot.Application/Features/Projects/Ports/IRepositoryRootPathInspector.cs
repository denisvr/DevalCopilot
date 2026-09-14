namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// The one place a candidate repository-root path is classified against the filesystem —
/// normalization, existence, reparse-point/UNC/filesystem-root policy, and any future
/// physical-identity resolution all live behind this port, in Infrastructure. Application
/// callers receive only a closed outcome and, on success, an opaque, already-safe candidate —
/// never a raw exception, an OS error string, an ancestor path, a reparse-point target, or any
/// path the caller did not itself supply.
/// </summary>
public interface IRepositoryRootPathInspector
{
    RepositoryRootInspectionResult Inspect(string requestedPath);
}

public enum RepositoryRootInspectionOutcome
{
    Success,

    /// <summary>The requested path is not fully qualified (relative, or otherwise not an
    /// absolute Windows path).</summary>
    NotAbsolute,

    /// <summary>The requested path is a UNC or other network path. Rejected for this local,
    /// single-workstation MVP — see ADR-0001 and ADR-0007.</summary>
    RemoteRootNotSupported,

    /// <summary>The requested path, once normalized, is itself a filesystem root (a drive root
    /// or share root). Rejected because a registered project's canonical root is intended to
    /// become the approved root for that project's future process/worktree operations — a
    /// filesystem root would make the entire volume an approved root.</summary>
    FilesystemRootNotSupported,

    /// <summary>The normalized path does not exist as a directory.</summary>
    PathNotFound,

    /// <summary>The path exists but could not be inspected (permission denied, or another I/O
    /// failure) — the caller's own filesystem exception is never propagated past this
    /// outcome.</summary>
    PathInaccessible,

    /// <summary>The root itself (not an ancestor) is a reparse point (a directory symlink or
    /// junction). Rejected for referential stability, independent of and in addition to the
    /// identity policy in ADR-0007: a reparse point could be silently repointed later,
    /// invalidating the registration without DevalCopilot's knowledge.</summary>
    ReparsePointNotSupported,
}

/// <summary>Populated only when <see cref="RepositoryRootInspectionResult.Outcome"/> is
/// <see cref="RepositoryRootInspectionOutcome.Success"/>. Computed once, by Infrastructure,
/// from the one normalization rule — Domain and Application never re-derive it.</summary>
/// <param name="CanonicalPath">The normalized, fully-qualified, display-cased path. The
/// registration-only identity key used for duplicate detection (see ADR-0007) is the pure
/// <c>CanonicalPath.ToUpperInvariant()</c> transform of this value, computed identically by
/// <c>Project.Register</c> and by the duplicate-check query — never a separate value threaded
/// through this type.</param>
public sealed record RepositoryRootCandidate(string CanonicalPath);

public sealed record RepositoryRootInspectionResult(
    RepositoryRootInspectionOutcome Outcome, RepositoryRootCandidate? Candidate);
