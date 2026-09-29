using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;

/// <summary>
/// One bounded page of a run's Agent attempts, newest first (descending, per-run unique
/// <c>AttemptNumber</c>). <paramref name="BeforeAttemptNumber"/> is an exclusive cursor: only
/// attempts with a strictly smaller number are returned, so the cursor stays stable while new
/// attempts are appended. <paramref name="Limit"/> is already bounded by the caller. Only
/// Agent-kind attempts are ever included. An unknown run fails with <c>runs.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptHistoryQuery(Guid RunId, int? BeforeAttemptNumber, int Limit)
    : IQuery<Result<GetAgentAttemptHistoryQueryResult>>;
