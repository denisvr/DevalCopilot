using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects;

/// <summary>
/// The one place a project's persisted physical identity is ever established or changed —
/// shared, not duplicated, by <c>RecheckProjectPhysicalIdentityCommandHandler</c> and
/// <c>PrepareRepositoryWorkspaceCommandHandler</c> so both entry points apply identical
/// semantics. A stable, independent-of-transport-shape policy: it depends only on the two
/// Infrastructure ports and the <see cref="Project"/> aggregate itself, mutating the tracked
/// entity in place and never touching persistence or the filesystem directly. See ADR-0008.
/// </summary>
internal static class PhysicalIdentityRecheck
{
    public static Result<PhysicalIdentityStatus> Apply(
        Project project,
        IRepositoryRootPathInspector rootPathInspector,
        IRepositoryPhysicalIdentityInspector physicalIdentityInspector)
    {
        var rootResult = rootPathInspector.Inspect(project.CanonicalPath);
        if (rootResult.Outcome != RepositoryRootInspectionOutcome.Success || rootResult.Candidate is null)
        {
            // Every root-level failure (not found, inaccessible, now a reparse point, ...)
            // collapses to the one closed reason that fits it — never the root inspector's own
            // specific outcome, nor any path/OS detail.
            return RecordUnavailable(project, PhysicalIdentityFailureReason.PathInaccessible);
        }

        var identityResult = physicalIdentityInspector.Resolve(rootResult.Candidate);
        if (identityResult.Outcome != RepositoryPhysicalIdentityInspectionOutcome.Resolved)
        {
            var reason = identityResult.Outcome == RepositoryPhysicalIdentityInspectionOutcome.UnsupportedFilesystem
                ? PhysicalIdentityFailureReason.UnsupportedFilesystem
                : PhysicalIdentityFailureReason.PathInaccessible;
            return RecordUnavailable(project, reason);
        }

        var volumeSerialNumber = identityResult.VolumeSerialNumber!.Value;
        var fileId = identityResult.FileId!;

        if (project.PhysicalIdentityStatus != PhysicalIdentityStatus.Resolved)
        {
            project.RecordPhysicalIdentityResolved(volumeSerialNumber, fileId);
            return Result<PhysicalIdentityStatus>.Success(PhysicalIdentityStatus.Resolved);
        }

        if (project.PhysicalIdentityMatches(volumeSerialNumber, fileId))
        {
            return Result<PhysicalIdentityStatus>.Success(PhysicalIdentityStatus.Resolved);
        }

        // Never silently overwritten: a different physical identity for an already-resolved
        // project could mean the directory was replaced, which needs a human's attention, not a
        // quiet identity swap.
        return Result<PhysicalIdentityStatus>.Failure(Error.Conflict(
            "projects.physical_identity_changed",
            "This path's physical identity has changed since it was last verified."));
    }

    private static Result<PhysicalIdentityStatus> RecordUnavailable(Project project, PhysicalIdentityFailureReason reason)
    {
        if (project.PhysicalIdentityStatus == PhysicalIdentityStatus.Resolved)
        {
            // A transient failure to verify never demotes an already-resolved identity — the
            // previously verified identity is kept exactly as-is.
            return Result<PhysicalIdentityStatus>.Failure(Error.Conflict(
                "projects.physical_identity_check_failed",
                "This project's physical identity could not be verified right now. Its previously verified identity was kept."));
        }

        project.RecordPhysicalIdentityUnavailable(reason);
        return Result<PhysicalIdentityStatus>.Success(PhysicalIdentityStatus.Unavailable);
    }
}
