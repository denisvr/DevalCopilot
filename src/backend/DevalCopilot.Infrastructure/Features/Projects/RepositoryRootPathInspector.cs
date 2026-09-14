using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// The one place a candidate repository-root path touches the filesystem or the <see
/// cref="Path"/> API. Every exception an ordinary filesystem condition can raise is caught here
/// and translated to a closed <see cref="RepositoryRootInspectionOutcome"/> — none of them, nor
/// any OS error text, ancestor path, or reparse-point target, is ever allowed to reach the
/// Application layer, API output, or logs. An exception escaping this type is a defect, not a
/// possible outcome.
///
/// <para>
/// This is also the seam where a future physical-identity resolution (Option A, deferred per
/// ADR-0007 — Windows volume serial number + file reference number, requiring
/// <c>CreateFile</c>/<c>GetFileInformationByHandle</c> P/Invoke since no BCL API exposes this
/// for directories) would be added, entirely inside this adapter, without changing Application
/// or Domain.
/// </para>
/// </summary>
public sealed class RepositoryRootPathInspector : IRepositoryRootPathInspector
{
    private static readonly RepositoryRootInspectionResult NotAbsolute =
        new(RepositoryRootInspectionOutcome.NotAbsolute, null);

    public RepositoryRootInspectionResult Inspect(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) || !Path.IsPathFullyQualified(requestedPath))
        {
            return NotAbsolute;
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return NotAbsolute;
        }

        // Covers both a true UNC share (\\server\share\...) and an explicit \\?\-prefixed
        // extended-length/device path — neither is a single, ordinary local-drive spelling, and
        // this MVP's canonical-identity policy assumes exactly one. A local path that
        // legitimately needs the extended-length prefix is not supported by this slice.
        if (canonicalPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.RemoteRootNotSupported, null);
        }

        // A registered project's canonical root becomes the future approved root for that
        // project's process/worktree operations — a filesystem root here would make an entire
        // volume an approved root.
        if (string.Equals(Path.GetPathRoot(canonicalPath), canonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            return new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.FilesystemRootNotSupported, null);
        }

        try
        {
            if (!Directory.Exists(canonicalPath))
            {
                return new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.PathNotFound, null);
            }

            if (File.GetAttributes(canonicalPath).HasFlag(FileAttributes.ReparsePoint))
            {
                return new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.ReparsePointNotSupported, null);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new RepositoryRootInspectionResult(RepositoryRootInspectionOutcome.PathInaccessible, null);
        }

        return new RepositoryRootInspectionResult(
            RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(canonicalPath));
    }
}
