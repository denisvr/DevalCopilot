namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// Resolves the Windows physical identity (volume serial number + 128-bit file ID) of an
/// already filesystem-validated candidate root. A distinct, narrow capability from
/// <see cref="IRepositoryRootPathInspector"/> and <see cref="IGitRepositoryInspector"/> — this
/// is the only port that ever touches the raw Win32 identity API. See ADR-0008.
/// </summary>
public interface IRepositoryPhysicalIdentityInspector
{
    RepositoryPhysicalIdentityInspectionResult Resolve(RepositoryRootCandidate candidate);
}

public enum RepositoryPhysicalIdentityInspectionOutcome
{
    Resolved,
    UnsupportedFilesystem,
    PathInaccessible,
}

/// <summary><paramref name="VolumeSerialNumber"/> and <paramref name="FileId"/> are set only
/// when <paramref name="Outcome"/> is <see cref="RepositoryPhysicalIdentityInspectionOutcome.Resolved"/>.
/// <paramref name="FileId"/> is exactly 16 bytes.</summary>
public sealed record RepositoryPhysicalIdentityInspectionResult(
    RepositoryPhysicalIdentityInspectionOutcome Outcome, ulong? VolumeSerialNumber, byte[]? FileId);
