using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;

public sealed class GetProjectVerificationInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectVerificationInspectionQuery, Result<GetProjectVerificationInspectionQueryResult>>
{
    public async Task<Result<GetProjectVerificationInspectionQueryResult>> HandleAsync(
        GetProjectVerificationInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await ProjectVerificationInspectionReader.ReadAsync(dbContext, query.ProjectId, query.ExecutionId, cancellationToken);
        if (reading.Kind == ProjectVerificationInspectionReadingKind.NotFound)
        {
            return Result<GetProjectVerificationInspectionQueryResult>.Failure(ProjectVerificationInspectionErrors.NotFound());
        }

        if (reading.Execution is not { } execution)
        {
            return Result<GetProjectVerificationInspectionQueryResult>.Success(new GetProjectVerificationInspectionQueryResult(
                query.ProjectId, query.ExecutionId, ProjectVerificationInspectionStatus.Unavailable, null));
        }

        // Metadata only, per exact execution and allowlisted stream: a damaged stream never hides its coherent sibling, and no path, hash
        // or byte is selected here.
        var standardOutput = await ProjectVerificationOutputReader.DescribeAsync(
            dbContext, query.ExecutionId, VerificationOutputPurpose.StandardOutput, cancellationToken);
        var standardError = await ProjectVerificationOutputReader.DescribeAsync(
            dbContext, query.ExecutionId, VerificationOutputPurpose.StandardError, cancellationToken);
        return Result<GetProjectVerificationInspectionQueryResult>.Success(new GetProjectVerificationInspectionQueryResult(
            query.ProjectId,
            query.ExecutionId,
            ProjectVerificationInspectionStatus.Recorded,
            new ProjectVerificationInspectionExecution(
                execution.Number,
                execution.CommandId,
                execution.CommandName,
                execution.WorkspaceId,
                execution.CheckpointId,
                execution.CheckpointNumber,
                execution.ClaimFingerprint,
                execution.CompletionFingerprint,
                execution.Status,
                execution.Outcome,
                execution.ExitCode,
                execution.ClaimedAtUtc,
                execution.DispatchedAtUtc,
                execution.CompletedAtUtc,
                standardOutput,
                standardError)));
    }
}
