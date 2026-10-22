using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;

public sealed class GetLocalExecutionSequenceInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionSequenceInspectionQuery, Result<GetLocalExecutionSequenceInspectionQueryResult>>
{
    public async Task<Result<GetLocalExecutionSequenceInspectionQueryResult>> HandleAsync(
        GetLocalExecutionSequenceInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await LocalExecutionInspectionReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        return reading.Kind switch
        {
            LocalExecutionInspectionReadingKind.RunNotFound =>
                Result<GetLocalExecutionSequenceInspectionQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound()),
            LocalExecutionInspectionReadingKind.NotRecorded => Succeed(query.RunId, LocalExecutionSequenceInspectionStatus.NotRecorded, null),
            LocalExecutionInspectionReadingKind.Unavailable => Succeed(query.RunId, LocalExecutionSequenceInspectionStatus.Unavailable, null),
            _ => Succeed(query.RunId, LocalExecutionSequenceInspectionStatus.Recorded, reading.Structure),
        };
    }

    private static Result<GetLocalExecutionSequenceInspectionQueryResult> Succeed(
        Guid runId, LocalExecutionSequenceInspectionStatus status, LocalExecutionInspectionStructure? structure) =>
        Result<GetLocalExecutionSequenceInspectionQueryResult>.Success(new GetLocalExecutionSequenceInspectionQueryResult(
            runId, status, structure?.Header, structure?.Steps ?? []));
}
