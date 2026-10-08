using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>Reads the recorded operation facts together with the recorded lease and the workspace and project locations, untracked.
/// Returns null when a recorded owner no longer exists, which callers treat as unprovable, never as absence of a promotion.</summary>
internal static class LocalCommitFactsReader
{
    public static async Task<LocalCommitFacts?> ReadAsync(
        IDevalCopilotDbContext dbContext, LocalCommitOperation operation, CancellationToken cancellationToken)
    {
        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.Id == operation.GitWorkspaceId)
            .Select(candidate => new { candidate.WorkspacePath, candidate.ProjectId })
            .SingleOrDefaultAsync(cancellationToken);
        var project = await dbContext.Projects.AsNoTracking()
            .Where(candidate => candidate.Id == operation.ProjectId)
            .Select(candidate => new { candidate.CanonicalPath })
            .SingleOrDefaultAsync(cancellationToken);
        var lease = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operation.RepositoryMutationLeaseId, cancellationToken);
        if (workspace is null || project is null || lease is null || lease.Status != LeaseStatus.Active
            || workspace.ProjectId != operation.ProjectId)
        {
            return null;
        }

        return new LocalCommitFacts(
            operation.Id,
            project.CanonicalPath,
            workspace.WorkspacePath,
            operation.BranchName,
            operation.ParentCommitSha,
            operation.TreeSha,
            operation.CommitSha,
            operation.CommitMessage,
            operation.AuthorName,
            operation.AuthorEmail,
            operation.CommitTimeUnixSeconds,
            operation.IndexPreimageSha256,
            operation.PreparedIndexSha256,
            operation.PreparedIndexRelativePath,
            new LocalCommitOwnership(
                operation.GitWorkspaceId, operation.ProjectId, lease.Id, lease.PhysicalVolumeSerialNumber,
                Convert.ToHexString(lease.PhysicalFileId)));
    }
}
