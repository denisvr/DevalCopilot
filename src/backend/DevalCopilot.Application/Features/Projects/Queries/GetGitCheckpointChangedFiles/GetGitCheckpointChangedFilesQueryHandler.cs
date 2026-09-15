using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointChangedFiles;

public sealed class GetGitCheckpointChangedFilesQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetGitCheckpointChangedFilesQuery, Result<IReadOnlyList<GitCheckpointChangedFileQueryResult>>>
{
    public async Task<Result<IReadOnlyList<GitCheckpointChangedFileQueryResult>>> HandleAsync(
        GetGitCheckpointChangedFilesQuery query, CancellationToken cancellationToken)
    {
        var belongsToProject = await (
                from checkpoint in dbContext.GitCheckpoints.AsNoTracking()
                join workspace in dbContext.GitWorkspaces.AsNoTracking() on checkpoint.WorkspaceId equals workspace.Id
                where checkpoint.Id == query.CheckpointId && workspace.ProjectId == query.ProjectId
                select checkpoint.Id)
            .AnyAsync(cancellationToken);
        if (!belongsToProject)
        {
            return Result<IReadOnlyList<GitCheckpointChangedFileQueryResult>>.Failure(
                Error.NotFound("git_checkpoints.not_found", "This source checkpoint does not exist for this project."));
        }

        var files = await dbContext.GitChangedFiles
            .AsNoTracking()
            .Where(file => file.CheckpointId == query.CheckpointId)
            .OrderBy(file => file.Path)
            .Select(file => new GitCheckpointChangedFileQueryResult(
                file.Path, file.PreviousPath, file.IndexStatus, file.WorkTreeStatus))
            .ToArrayAsync(cancellationToken);

        return Result<IReadOnlyList<GitCheckpointChangedFileQueryResult>>.Success(files);
    }
}
