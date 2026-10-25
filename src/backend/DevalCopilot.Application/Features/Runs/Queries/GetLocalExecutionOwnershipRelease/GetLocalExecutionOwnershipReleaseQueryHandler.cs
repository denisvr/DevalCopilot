using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>
/// Reads one step's release candidate or receipt with exactly one database statement and no external read (ADR-0065). A candidate carries
/// the versioned confirmation whose expiry is at most two minutes after this read; a stored value this build cannot read is Unavailable,
/// never a plausible answer. It writes nothing and changes no ownership, fence or capacity reading.
/// </summary>
public sealed class GetLocalExecutionOwnershipReleaseQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetLocalExecutionOwnershipReleaseQuery, Result<GetLocalExecutionOwnershipReleaseQueryResult>>
{
    public async Task<Result<GetLocalExecutionOwnershipReleaseQueryResult>> HandleAsync(
        GetLocalExecutionOwnershipReleaseQuery query, CancellationToken cancellationToken)
    {
        LocalExecutionReleaseReading reading;
        try
        {
            reading = await LocalExecutionReleaseReader.ReadAsync(dbContext, query.RunId, query.StepId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            // A stored value this build cannot materialize: nothing is disclosed and the hold stays.
            reading = LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        if (reading.Kind == LocalExecutionReleaseReadingKind.NotFound)
        {
            return Result<GetLocalExecutionOwnershipReleaseQueryResult>.Failure(LocalExecutionOwnershipReleaseErrors.NotFound());
        }

        return Result<GetLocalExecutionOwnershipReleaseQueryResult>.Success(Map(query, reading, timeProvider.GetUtcNow()));
    }

    private static GetLocalExecutionOwnershipReleaseQueryResult Map(
        GetLocalExecutionOwnershipReleaseQuery query, LocalExecutionReleaseReading reading, DateTimeOffset nowUtc)
    {
        switch (reading.Kind)
        {
            case LocalExecutionReleaseReadingKind.Available:
                {
                    var facts = reading.Facts!;
                    var expiresAtUtc = LocalExecutionOwnershipReleaseConfirmation.ExpiryOf(nowUtc);
                    return new GetLocalExecutionOwnershipReleaseQueryResult(
                        query.RunId,
                        query.StepId,
                        LocalExecutionOwnershipReleaseStatus.Available,
                        null,
                        new LocalExecutionOwnershipReleaseCandidate(
                            facts,
                            LocalExecutionOwnershipReleaseConfirmation.CurrentVersion,
                            LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiresAtUtc),
                            expiresAtUtc),
                        null);
                }

            case LocalExecutionReleaseReadingKind.Released:
                return new GetLocalExecutionOwnershipReleaseQueryResult(
                    query.RunId,
                    query.StepId,
                    LocalExecutionOwnershipReleaseStatus.Released,
                    null,
                    null,
                    new LocalExecutionOwnershipReleaseReceiptView(
                        reading.Facts!, reading.Recorded!.ReleaseId, reading.Recorded.ReleasedAtUtc, reading.Recorded.EventSequence));

            case LocalExecutionReleaseReadingKind.NotEligible:
                return new GetLocalExecutionOwnershipReleaseQueryResult(
                    query.RunId,
                    query.StepId,
                    LocalExecutionOwnershipReleaseStatus.NotEligible,
                    LocalExecutionReleaseReasons.CodeOf(reading.Reason),
                    null,
                    null);

            default:
                return new GetLocalExecutionOwnershipReleaseQueryResult(
                    query.RunId,
                    query.StepId,
                    LocalExecutionOwnershipReleaseStatus.Unavailable,
                    LocalExecutionReleaseReasons.CodeOf(reading.Reason),
                    null,
                    null);
        }
    }
}
