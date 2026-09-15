using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Reads and atomically writes the durable ownership marker. A write creates a uniquely named
/// temporary file in the same administrative directory, flushes and closes it, then replaces
/// the fixed final name in one atomic same-volume rename (<c>File.Move</c> with
/// <c>overwrite: true</c>, backed by Windows' atomic <c>MOVEFILE_REPLACE_EXISTING</c>) — the
/// final name is never observed partially written. Validation is the caller's job: this store
/// only parses the file into typed fields or reports it absent/invalid; it never compares raw
/// bytes.
/// </summary>
public sealed class WorkspaceOwnershipMarkerStore : IWorkspaceOwnershipMarkerStore
{
    private const string MarkerFileName = "devalcopilot-ownership.json";
    private const string SchemaVersion = "1";

    public async Task<WorkspaceOwnershipMarkerWriteResult> WriteAsync(
        string administrativeDirectory, WorkspaceOwnershipMarker marker, CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(administrativeDirectory, MarkerFileName);
        var temporaryPath = Path.Combine(administrativeDirectory, $"{MarkerFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var dto = new MarkerDto(
                SchemaVersion, marker.WorkspaceId, marker.ProjectId, marker.LeaseId,
                marker.PhysicalVolumeSerialNumber, marker.PhysicalFileIdHex);
            var json = JsonSerializer.Serialize(dto);

            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);

            return new WorkspaceOwnershipMarkerWriteResult(WorkspaceOwnershipMarkerWriteOutcome.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            return new WorkspaceOwnershipMarkerWriteResult(WorkspaceOwnershipMarkerWriteOutcome.WriteFailed);
        }
    }

    public async Task<WorkspaceOwnershipMarkerReadResult> ReadAsync(string administrativeDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(administrativeDirectory, MarkerFileName);

        try
        {
            if (!File.Exists(path))
            {
                return new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null);
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var dto = JsonSerializer.Deserialize<MarkerDto>(json);

            if (dto is null || string.IsNullOrEmpty(dto.PhysicalFileIdHex) || dto.WorkspaceId == Guid.Empty)
            {
                return new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Invalid, null);
            }

            var marker = new WorkspaceOwnershipMarker(
                dto.WorkspaceId, dto.ProjectId, dto.LeaseId, dto.PhysicalVolumeSerialNumber, dto.PhysicalFileIdHex);
            return new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Valid, marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Invalid, null);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort only: an orphaned .tmp file beside the marker is inert and never
            // adopted as the marker itself (the fixed final name is what every reader checks).
        }
    }

    private sealed record MarkerDto(
        string SchemaVersion,
        Guid WorkspaceId,
        Guid ProjectId,
        Guid LeaseId,
        ulong PhysicalVolumeSerialNumber,
        string PhysicalFileIdHex);
}
