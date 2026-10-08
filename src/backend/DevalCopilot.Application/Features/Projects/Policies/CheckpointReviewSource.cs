using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The source a human review or its approval evidence is decided against: the project, its latest workspace and that workspace's
/// exact current checkpoint, read untracked and decided by the existing gates (a ready workspace with an active lease, and a selected
/// checkpoint that is the current one). The review command and the approval-evidence query share it so both always agree on the
/// source they accept; a workspace under a local-commit reservation is never Ready and so never reviewable.
/// </summary>
internal static class CheckpointReviewSource
{
    internal sealed record Authority(
        Guid ProjectId, Guid WorkspaceId, string WorkspacePath, Guid CheckpointId, int CheckpointNumber, string FingerprintSha256);

    internal sealed record Read(Error? Error, Authority? Source);

    public static Error NotFound() => Error.NotFound("reviews.not_found", "The project or isolated workspace was not found.");

    public static Error CheckpointNotCurrent() =>
        Error.Conflict("reviews.checkpoint_not_current", "The selected source checkpoint is no longer current.");

    public static async Task<Read> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid checkpointId, CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Projects.AsNoTracking().AnyAsync(project => project.Id == projectId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == projectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .Select(candidate => new { candidate.Id, candidate.Status, candidate.WorkspacePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (!projectExists || workspace is null)
        {
            return new Read(NotFound(), null);
        }

        if (workspace.Status != WorkspaceStatus.Ready || !await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return new Read(
                Error.Conflict("reviews.workspace_not_ready", "A ready, owned workspace is required to record a review."), null);
        }

        var current = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => new { candidate.Id, candidate.CheckpointNumber, candidate.FingerprintSha256 })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null || current.Id != checkpointId)
        {
            return new Read(
                Error.Conflict("reviews.checkpoint_not_current", "Only the current source checkpoint may be reviewed."), null);
        }

        return new Read(
            null,
            new Authority(projectId, workspace.Id, workspace.WorkspacePath, current.Id, current.CheckpointNumber, current.FingerprintSha256));
    }
}
