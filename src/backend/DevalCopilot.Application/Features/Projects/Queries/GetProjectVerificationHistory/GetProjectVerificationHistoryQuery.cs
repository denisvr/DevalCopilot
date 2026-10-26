using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

/// <summary>
/// One fixed 20-member keyset page of a project's recorded verification executions, newest first (ADR-0069). A null
/// <paramref name="BeforeExecutionNumber"/> reads the newest page; otherwise it is a positive Int32 and only strictly older executions
/// are read. Anything else is refused, never clamped. An unknown project fails with <c>project_verification_inspection.not_found</c>.
/// </summary>
public sealed record GetProjectVerificationHistoryQuery(Guid ProjectId, int? BeforeExecutionNumber)
    : IQuery<Result<GetProjectVerificationHistoryQueryResult>>
{
    public const int PageSize = 20;
}
