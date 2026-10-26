using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;

public sealed class GetPlanningSequenceInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetPlanningSequenceInspectionQuery, Result<GetPlanningSequenceInspectionQueryResult>>
{
    public async Task<Result<GetPlanningSequenceInspectionQueryResult>> HandleAsync(
        GetPlanningSequenceInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await PlanningSequenceInspectionReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        return reading.Kind switch
        {
            PlanningSequenceInspectionReadingKind.RunNotFound =>
                Result<GetPlanningSequenceInspectionQueryResult>.Failure(PlanningSequenceInspectionErrors.NotFound()),
            PlanningSequenceInspectionReadingKind.NotRecorded => Succeed(query.RunId, PlanningSequenceInspectionStatus.NotRecorded, null),
            PlanningSequenceInspectionReadingKind.Unavailable => Succeed(query.RunId, PlanningSequenceInspectionStatus.Unavailable, null),
            _ => Succeed(query.RunId, PlanningSequenceInspectionStatus.Recorded, reading.Structure),
        };
    }

    private static Result<GetPlanningSequenceInspectionQueryResult> Succeed(
        Guid runId, PlanningSequenceInspectionStatus status, PlanningSequenceInspectionStructure? structure) =>
        Result<GetPlanningSequenceInspectionQueryResult>.Success(new GetPlanningSequenceInspectionQueryResult(
            runId, status, structure?.Header, structure?.Steps ?? []));
}
