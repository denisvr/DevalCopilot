using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>
/// One bounded page of a run's persisted event headers, newest first by the database-assigned <c>Sequence</c> (ADR-0034).
/// <paramref name="BeforeSequence"/> is an exclusive cursor: absent, or a canonical positive decimal that fits a signed 64-bit
/// integer (a string because a browser number cannot carry that range). <paramref name="Limit"/> defaults to
/// <see cref="DefaultLimit"/> and accepts 1 to <see cref="MaximumLimit"/>; an out-of-range value is refused, never clamped. It reads
/// header columns only and writes, claims and repairs nothing. An unknown run fails with <c>runs.not_found</c>.
/// </summary>
public sealed record GetRunEventHistoryQuery(Guid RunId, string? BeforeSequence, int? Limit)
    : IQuery<Result<GetRunEventHistoryQueryResult>>
{
    public const int DefaultLimit = 20;

    public const int MaximumLimit = 50;
}
