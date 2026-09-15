using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitEvidence;

public sealed class GetProjectGitEvidenceQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectGitEvidenceQuery, Result<GetProjectGitEvidenceQueryResult>>
{
    public async Task<Result<GetProjectGitEvidenceQueryResult>> HandleAsync(
        GetProjectGitEvidenceQuery query, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Projects.AnyAsync(project => project.Id == query.ProjectId, cancellationToken);
        if (!exists)
        {
            return Result<GetProjectGitEvidenceQueryResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var checkpoint = await (
                from workspace in dbContext.GitWorkspaces.AsNoTracking()
                join candidate in dbContext.GitCheckpoints.AsNoTracking() on workspace.Id equals candidate.WorkspaceId
                where workspace.ProjectId == query.ProjectId
                orderby workspace.WorkspaceNumber descending, candidate.CheckpointNumber descending
                select new
                {
                    candidate.Id,
                    candidate.CheckpointNumber,
                    candidate.CapturedAtUtc,
                    candidate.HeadCommitSha,
                    candidate.FingerprintSha256,
                    ChangedFileCount = dbContext.GitChangedFiles.Count(file => file.CheckpointId == candidate.Id),
                })
            .FirstOrDefaultAsync(cancellationToken);

        return Result<GetProjectGitEvidenceQueryResult>.Success(checkpoint is null
            ? new(null, null, null, null, null, 0)
            : new(checkpoint.Id, checkpoint.CheckpointNumber, checkpoint.CapturedAtUtc, checkpoint.HeadCommitSha,
                checkpoint.FingerprintSha256, checkpoint.ChangedFileCount));
    }
}
