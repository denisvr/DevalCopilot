using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;

/// <summary>
/// Reads one step's recorded release receipt with exactly one database statement and no external read (ADR-0067), through the same
/// statement and proof the release candidate uses but without its candidate, confirmation or clock. It writes nothing and changes no
/// ownership, fence or capacity reading. A stored value this build cannot materialize is Unavailable, never absence.
/// </summary>
public sealed class GetLocalExecutionReleaseInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionReleaseInspectionQuery, Result<GetLocalExecutionReleaseInspectionQueryResult>>
{
    public async Task<Result<GetLocalExecutionReleaseInspectionQueryResult>> HandleAsync(
        GetLocalExecutionReleaseInspectionQuery query, CancellationToken cancellationToken)
    {
        LocalExecutionReleaseInspectionReading reading;
        try
        {
            reading = await LocalExecutionReleaseInspectionReader.ReadAsync(dbContext, query.RunId, query.StepId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            // A stored value this build cannot materialize: whether a receipt exists is unknown, so nothing is disclosed or denied.
            reading = LocalExecutionReleaseInspectionReading.Unavailable;
        }

        if (reading.Kind == LocalExecutionReleaseInspectionReadingKind.NotFound)
        {
            return Result<GetLocalExecutionReleaseInspectionQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound());
        }

        var status = reading.Kind switch
        {
            LocalExecutionReleaseInspectionReadingKind.Recorded => LocalExecutionReleaseInspectionStatus.Recorded,
            LocalExecutionReleaseInspectionReadingKind.NotRecorded => LocalExecutionReleaseInspectionStatus.NotRecorded,
            _ => LocalExecutionReleaseInspectionStatus.Unavailable,
        };
        var receipt = reading.Kind == LocalExecutionReleaseInspectionReadingKind.Recorded
            ? new LocalExecutionReleaseInspectionReceipt(
                reading.Facts!, reading.Recorded!.ReleaseId, reading.Recorded.ReleasedAtUtc, reading.Recorded.EventSequence)
            : null;
        return Result<GetLocalExecutionReleaseInspectionQueryResult>.Success(
            new GetLocalExecutionReleaseInspectionQueryResult(query.RunId, query.StepId, status, receipt));
    }
}
