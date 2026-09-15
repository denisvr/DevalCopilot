using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;

/// <summary>
/// Projects a project's physical-identity status and its most recent
/// <see cref="GitWorkspace"/> (by <see cref="GitWorkspace.WorkspaceNumber"/>, never by a
/// timestamp) and lease. Reflects only durable state — a request that was blocked before
/// anything persisted (a dirty baseline, a missing precondition) never appears here; that
/// reason is returned directly by the preparation command's own response.
/// </summary>
public sealed class GetProjectWorkspaceQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectWorkspaceQuery, Result<GetProjectWorkspaceQueryResult>>
{
    public async Task<Result<GetProjectWorkspaceQueryResult>> HandleAsync(
        GetProjectWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .Where(candidate => candidate.Id == query.ProjectId)
            .Select(candidate => new { candidate.PhysicalIdentityStatus, candidate.PhysicalIdentityFailureReason })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return Result<GetProjectWorkspaceQueryResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var physicalIdentityBlockedMessage = project.PhysicalIdentityStatus == PhysicalIdentityStatus.Unavailable
            ? MapPhysicalIdentityFailureMessage(project.PhysicalIdentityFailureReason)
            : null;

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == query.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (workspace is null)
        {
            return Result<GetProjectWorkspaceQueryResult>.Success(new GetProjectWorkspaceQueryResult(
                project.PhysicalIdentityStatus.ToString(), physicalIdentityBlockedMessage,
                WorkspacePreparationState.NotRequested, null, null, null, null, null, null, null));
        }

        var leaseStatus = await dbContext.RepositoryMutationLeases
            .AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .Select(candidate => (LeaseStatus?)candidate.Status)
            .SingleOrDefaultAsync(cancellationToken);

        var state = workspace.Status switch
        {
            WorkspaceStatus.Preparing => WorkspacePreparationState.Preparing,
            WorkspaceStatus.Ready => WorkspacePreparationState.Ready,
            WorkspaceStatus.NeedsAttention => WorkspacePreparationState.NeedsAttention,
            _ => WorkspacePreparationState.Blocked,
        };

        var showCandidatePath = state is WorkspacePreparationState.Ready or WorkspacePreparationState.NeedsAttention;

        return Result<GetProjectWorkspaceQueryResult>.Success(new GetProjectWorkspaceQueryResult(
            project.PhysicalIdentityStatus.ToString(),
            physicalIdentityBlockedMessage,
            state,
            showCandidatePath ? workspace.WorkspacePath : null,
            showCandidatePath ? workspace.BranchName : null,
            showCandidatePath ? workspace.SourceCommitSha : null,
            showCandidatePath ? workspace.SourceBranchName : null,
            leaseStatus?.ToString(),
            workspace.LastFailureReasonCode,
            workspace.LastFailureReasonCode is null ? null : MapReasonMessage(workspace.LastFailureReasonCode)));
    }

    /// <summary>A fixed, safe display string per closed <see cref="PhysicalIdentityFailureReason"/>
    /// value — never a raw Win32 error, exception message, or path.</summary>
    private static string MapPhysicalIdentityFailureMessage(PhysicalIdentityFailureReason reason) => reason switch
    {
        PhysicalIdentityFailureReason.UnsupportedFilesystem =>
            "This repository's filesystem does not support the identity checks workspace preparation requires.",
        PhysicalIdentityFailureReason.PathInaccessible => "This project's path could not be verified.",
        _ => "This project's physical identity has not been verified yet.",
    };

    /// <summary>A fixed, safe display string per known workspace reason code — never the code
    /// itself shown raw, and never anything the underlying failure (an OS error, a path, an
    /// exception) might have carried. An unrecognized code still renders a safe, generic
    /// fallback rather than nothing.</summary>
    private static string MapReasonMessage(string reasonCode) => reasonCode switch
    {
        "workspaces.path_already_exists" => "A workspace already exists at the computed path.",
        "workspaces.path_overlaps_repository" => "This workspace's computed path conflicts with the repository's own path.",
        "workspaces.git_unavailable" => "Git was not available on this host while preparing this workspace.",
        "workspaces.git_invocation_timed_out" => "Creating this workspace took too long.",
        "workspaces.git_invocation_failed" => "This workspace could not be created.",
        "workspaces.administrative_directory_not_resolved" => "This workspace's Git administrative directory could not be verified.",
        "workspaces.marker_write_failed" => "This workspace's ownership marker could not be written.",
        "workspaces.reconciliation_worktree_missing" => "This workspace's files are no longer present on disk.",
        "workspaces.reconciliation_administrative_directory_unresolved" => "This workspace's Git administrative directory could no longer be verified.",
        "workspaces.reconciliation_marker_invalid" => "This workspace's ownership marker is missing or was altered outside DevalCopilot.",
        "workspaces.reconciliation_inconsistent_evidence" => "This workspace could not be verified as complete after a restart.",
        "workspaces.reconciliation_head_diverged" => "This workspace's content changed outside DevalCopilot and needs review.",
        _ => "This workspace could not be prepared.",
    };
}
