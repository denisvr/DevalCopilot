using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;

public sealed class GetLocalExecutionAdmissionHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionAdmissionHistoryQuery, Result<GetLocalExecutionAdmissionHistoryQueryResult>>
{
    public async Task<Result<GetLocalExecutionAdmissionHistoryQueryResult>> HandleAsync(
        GetLocalExecutionAdmissionHistoryQuery query, CancellationToken cancellationToken)
    {
        var reading = await LocalExecutionAdmissionHistoryReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        return reading.Kind switch
        {
            LocalExecutionAdmissionHistoryReadingKind.RunNotFound =>
                Result<GetLocalExecutionAdmissionHistoryQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound()),
            LocalExecutionAdmissionHistoryReadingKind.NotRecorded => Succeed(query.RunId, LocalExecutionAdmissionHistoryStatus.NotRecorded, reading),
            LocalExecutionAdmissionHistoryReadingKind.NotQueued => Succeed(query.RunId, LocalExecutionAdmissionHistoryStatus.NotQueued, reading),
            LocalExecutionAdmissionHistoryReadingKind.Unavailable => Succeed(query.RunId, LocalExecutionAdmissionHistoryStatus.Unavailable, reading),
            _ => Succeed(query.RunId, LocalExecutionAdmissionHistoryStatus.Recorded, reading),
        };
    }

    private static Result<GetLocalExecutionAdmissionHistoryQueryResult> Succeed(
        Guid runId, LocalExecutionAdmissionHistoryStatus status, LocalExecutionAdmissionHistoryReading reading) =>
        Result<GetLocalExecutionAdmissionHistoryQueryResult>.Success(
            new GetLocalExecutionAdmissionHistoryQueryResult(runId, status, reading.Header, reading.Admissions));
}
