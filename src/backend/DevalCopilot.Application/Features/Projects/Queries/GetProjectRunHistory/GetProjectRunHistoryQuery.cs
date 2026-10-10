using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

/// <summary>
/// One bounded page of a project's recorded runs, newest first (descending, per-project unique <c>ExecutionNumber</c>) (ADR-0033).
/// <paramref name="BeforeExecutionNumber"/> is an exclusive cursor, positive when supplied, so it stays stable while newer runs are
/// created. <paramref name="Limit"/> defaults to <see cref="DefaultLimit"/> and accepts 1 to <see cref="MaximumLimit"/>; an out-of-range
/// value is refused, never clamped. It reads persisted facts only and writes, claims and repairs nothing. An unknown project fails
/// with <c>projects.not_found</c>.
/// </summary>
public sealed record GetProjectRunHistoryQuery(Guid ProjectId, int? BeforeExecutionNumber, int? Limit)
    : IQuery<Result<GetProjectRunHistoryQueryResult>>
{
    public const int DefaultLimit = 10;

    public const int MaximumLimit = 20;
}
