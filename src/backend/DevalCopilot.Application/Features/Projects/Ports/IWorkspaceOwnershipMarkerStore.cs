namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// Reads and atomically writes the durable ownership marker inside a workspace's own Git
/// administrative directory (never inside either working tree). Written by writing a temporary
/// file, flushing/closing it, then atomically moving/replacing it as
/// <c>devalcopilot-ownership.json</c> — never a partially-written marker is observable at that
/// final name. Validation is typed-field comparison, performed by the caller against the marker
/// this store returns — never a raw byte/hash comparison. See ADR-0008.
/// </summary>
public interface IWorkspaceOwnershipMarkerStore
{
    Task<WorkspaceOwnershipMarkerWriteResult> WriteAsync(
        string administrativeDirectory, WorkspaceOwnershipMarker marker, CancellationToken cancellationToken);

    Task<WorkspaceOwnershipMarkerReadResult> ReadAsync(string administrativeDirectory, CancellationToken cancellationToken);
}

/// <summary><paramref name="PhysicalFileIdHex"/> is the 16-byte physical file ID, hex-encoded —
/// a plain, safe, ASCII-only representation for JSON.</summary>
public sealed record WorkspaceOwnershipMarker(
    Guid WorkspaceId, Guid ProjectId, Guid LeaseId, ulong PhysicalVolumeSerialNumber, string PhysicalFileIdHex);

public enum WorkspaceOwnershipMarkerWriteOutcome
{
    Success,
    WriteFailed,
}

public sealed record WorkspaceOwnershipMarkerWriteResult(WorkspaceOwnershipMarkerWriteOutcome Outcome);

public enum WorkspaceOwnershipMarkerReadOutcome
{
    /// <summary>The marker file parsed successfully. Field-by-field agreement against the
    /// durable database record is the caller's responsibility, not this store's.</summary>
    Valid,

    /// <summary>The file does not exist.</summary>
    Absent,

    /// <summary>The file exists but could not be parsed into the expected schema.</summary>
    Invalid,
}

/// <paramref name="Marker"/> is set only when <paramref name="Outcome"/> is
/// <see cref="WorkspaceOwnershipMarkerReadOutcome.Valid"/>.
public sealed record WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome Outcome, WorkspaceOwnershipMarker? Marker);
