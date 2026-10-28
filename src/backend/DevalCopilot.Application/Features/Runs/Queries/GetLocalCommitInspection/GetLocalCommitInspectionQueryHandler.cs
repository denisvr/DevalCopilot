using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;

/// <summary>
/// Reads one run's recorded local-commit operation with exactly one database statement and no external read (ADR-0070). It writes
/// nothing, consults no current eligibility, lease, workspace state, run lifecycle or stage, and swallows no database fault: a fault
/// propagates to the normal failure boundary and is never relabelled Unavailable or absent.
/// </summary>
public sealed class GetLocalCommitInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalCommitInspectionQuery, Result<GetLocalCommitInspectionQueryResult>>
{
    public async Task<Result<GetLocalCommitInspectionQueryResult>> HandleAsync(
        GetLocalCommitInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await RecordedLocalCommitReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        return reading.Kind switch
        {
            RecordedLocalCommitReadingKind.NotFound =>
                Result<GetLocalCommitInspectionQueryResult>.Failure(LocalCommitInspectionErrors.NotFound()),
            RecordedLocalCommitReadingKind.NotRecorded => Succeed(query.RunId, LocalCommitInspectionStatus.NotRecorded, null),
            RecordedLocalCommitReadingKind.Unavailable => Succeed(query.RunId, LocalCommitInspectionStatus.Unavailable, null),
            _ => Succeed(query.RunId, LocalCommitInspectionStatus.Recorded, reading.Operation),
        };
    }

    private static Result<GetLocalCommitInspectionQueryResult> Succeed(
        Guid runId, LocalCommitInspectionStatus status, RecordedLocalCommitOperation? operation) =>
        Result<GetLocalCommitInspectionQueryResult>.Success(new GetLocalCommitInspectionQueryResult(runId, status, operation));
}
