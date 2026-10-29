using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;

/// <summary>
/// Reads one run's recorded abandonment with exactly one database statement and no external read (ADR-0071). It writes nothing,
/// consults no present eligibility, blocker, workspace or lease and swallows no database fault: a fault propagates to the normal failure
/// boundary and is never relabelled Unavailable or absent.
/// </summary>
public sealed class GetRunAbandonmentInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunAbandonmentInspectionQuery, Result<GetRunAbandonmentInspectionQueryResult>>
{
    public async Task<Result<GetRunAbandonmentInspectionQueryResult>> HandleAsync(
        GetRunAbandonmentInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await RecordedAbandonmentReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        return reading.Kind switch
        {
            RecordedAbandonmentReadingKind.NotFound =>
                Result<GetRunAbandonmentInspectionQueryResult>.Failure(RunAbandonmentInspectionErrors.NotFound()),
            RecordedAbandonmentReadingKind.NotRecorded => Succeed(query.RunId, RunAbandonmentInspectionStatus.NotRecorded, null),
            RecordedAbandonmentReadingKind.Unavailable => Succeed(query.RunId, RunAbandonmentInspectionStatus.Unavailable, null),
            _ => Succeed(query.RunId, RunAbandonmentInspectionStatus.Recorded, reading.Abandonment),
        };
    }

    private static Result<GetRunAbandonmentInspectionQueryResult> Succeed(
        Guid runId, RunAbandonmentInspectionStatus status, RecordedAbandonment? abandonment) =>
        Result<GetRunAbandonmentInspectionQueryResult>.Success(new GetRunAbandonmentInspectionQueryResult(runId, status, abandonment));
}
