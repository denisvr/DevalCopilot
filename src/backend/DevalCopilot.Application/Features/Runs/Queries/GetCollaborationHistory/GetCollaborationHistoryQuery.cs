using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>
/// One bounded page of a run's recorded collaboration messages, newest first by the database-assigned <c>Sequence</c>
/// (ADR-0035). <paramref name="BeforeSequence"/> is an exclusive cursor: absent, or a canonical positive decimal that fits a signed
/// 64-bit integer (a string because a browser number cannot carry that range). <paramref name="Limit"/> defaults to
/// <see cref="DefaultLimit"/> and accepts 1 to <see cref="MaximumLimit"/>; an out-of-range value is refused, never clamped. It
/// writes, claims and repairs nothing. An unknown run fails with <c>runs.not_found</c>.
/// </summary>
public sealed record GetCollaborationHistoryQuery(Guid RunId, string? BeforeSequence, int? Limit)
    : IQuery<Result<GetCollaborationHistoryQueryResult>>
{
    public const int DefaultLimit = 10;

    public const int MaximumLimit = 20;
}
