using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetVerificationExecutionOutput;

public sealed class GetVerificationExecutionOutputQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetVerificationExecutionOutputQuery, Result<VerificationExecutionOutputQueryResult>>
{
    public async Task<Result<VerificationExecutionOutputQueryResult>> HandleAsync(
        GetVerificationExecutionOutputQuery query, CancellationToken cancellationToken)
    {
        var artifact = await dbContext.VerificationOutputArtifacts
            .Where(candidate => candidate.VerificationExecutionId == query.VerificationExecutionId)
            .Join(dbContext.VerificationExecutions.Where(execution => execution.ProjectId == query.ProjectId),
                artifact => artifact.VerificationExecutionId, execution => execution.Id, (artifact, _) => artifact)
            .SingleOrDefaultAsync(candidate => candidate.Purpose == query.Purpose, cancellationToken);
        if (artifact is null)
        {
            return Result<VerificationExecutionOutputQueryResult>.Failure(
                Error.NotFound("verification.output_not_found", "This verification output is not available."));
        }

        var window = await artifactStore.VerifyAndReadSealedAsync(
            artifact.RelativeStoragePath, artifact.ByteLength, artifact.ContentHash, query.FromOffset, query.MaxBytes, cancellationToken);
        if (window.Status != SealedReadStatus.Ok)
        {
            return Result<VerificationExecutionOutputQueryResult>.Failure(
                Error.Conflict("verification.output_unavailable", "This verification output could not be verified."));
        }

        return Result<VerificationExecutionOutputQueryResult>.Success(new(window.Text, window.NextOffset, window.TotalLengthSoFar, true));
    }
}
