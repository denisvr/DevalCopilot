using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationExecutions;

public sealed class GetProjectVerificationExecutionsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectVerificationExecutionsQuery, IReadOnlyList<VerificationExecutionQueryResult>>
{
    private const int MaximumResults = 20;

    public async Task<IReadOnlyList<VerificationExecutionQueryResult>> HandleAsync(
        GetProjectVerificationExecutionsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.VerificationExecutions
            .Where(execution => execution.ProjectId == query.ProjectId)
            .OrderByDescending(execution => execution.ExecutionNumber)
            .Take(MaximumResults)
            .Select(execution => new VerificationExecutionQueryResult(
                execution.Id,
                execution.VerificationCommandId,
                execution.ExecutionNumber,
                execution.Status,
                execution.DispatchedAtUtc.HasValue,
                execution.Outcome,
                execution.ExitCode,
                dbContext.VerificationOutputArtifacts.Any(artifact =>
                    artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardOutput),
                dbContext.VerificationOutputArtifacts.Any(artifact =>
                    artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardError),
                dbContext.VerificationOutputArtifacts
                    .Where(artifact => artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardOutput)
                    .Select(artifact => artifact.Truncated)
                    .SingleOrDefault(),
                dbContext.VerificationOutputArtifacts
                    .Where(artifact => artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardError)
                    .Select(artifact => artifact.Truncated)
                    .SingleOrDefault(),
                dbContext.VerificationOutputArtifacts
                    .Where(artifact => artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardOutput)
                    .Select(artifact => (VerificationOutputCaptureOutcome?)artifact.CaptureOutcome)
                    .SingleOrDefault(),
                dbContext.VerificationOutputArtifacts
                    .Where(artifact => artifact.VerificationExecutionId == execution.Id && artifact.Purpose == VerificationOutputPurpose.StandardError)
                    .Select(artifact => (VerificationOutputCaptureOutcome?)artifact.CaptureOutcome)
                    .SingleOrDefault(),
                execution.ClaimedAtUtc,
                execution.CompletedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
