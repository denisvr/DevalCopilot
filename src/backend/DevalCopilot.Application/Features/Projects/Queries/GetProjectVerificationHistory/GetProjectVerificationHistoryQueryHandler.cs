using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

public sealed class GetProjectVerificationHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectVerificationHistoryQuery, Result<GetProjectVerificationHistoryQueryResult>>
{
    public async Task<Result<GetProjectVerificationHistoryQueryResult>> HandleAsync(
        GetProjectVerificationHistoryQuery query, CancellationToken cancellationToken)
    {
        // The validator refuses this before dispatch; the handler enforces it again so no caller can read outside it.
        if (query.BeforeExecutionNumber is < 1)
        {
            return Result<GetProjectVerificationHistoryQueryResult>.Failure(new ValidationError(
                ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberCode,
                ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberMessage,
                nameof(GetProjectVerificationHistoryQuery.BeforeExecutionNumber)));
        }

        var reading = await ProjectVerificationHistoryReader.ReadAsync(
            dbContext, query.ProjectId, query.BeforeExecutionNumber, cancellationToken);
        return reading.Kind switch
        {
            ProjectVerificationInspectionReadingKind.NotFound =>
                Result<GetProjectVerificationHistoryQueryResult>.Failure(ProjectVerificationInspectionErrors.NotFound()),
            ProjectVerificationInspectionReadingKind.Unavailable => Result<GetProjectVerificationHistoryQueryResult>.Success(
                new GetProjectVerificationHistoryQueryResult(query.ProjectId, ProjectVerificationHistoryStatus.Unavailable, [], false, null)),
            _ => Result<GetProjectVerificationHistoryQueryResult>.Success(new GetProjectVerificationHistoryQueryResult(
                query.ProjectId,
                ProjectVerificationHistoryStatus.Recorded,
                reading.Entries
                    .Select(entry => new ProjectVerificationHistoryItem(
                        entry.ExecutionId, entry.ExecutionNumber, entry.CommandName, entry.ClaimedAtUtc))
                    .ToArray(),
                reading.HasMore,
                reading.NextBeforeExecutionNumber)),
        };
    }
}
