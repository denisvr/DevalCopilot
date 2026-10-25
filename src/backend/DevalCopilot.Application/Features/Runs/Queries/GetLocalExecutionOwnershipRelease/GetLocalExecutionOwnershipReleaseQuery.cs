using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>
/// The recorded ownership-release candidate or receipt of one step of one run's local execution sequence (ADR-0065). One bounded database
/// statement proves the exact historical chain; there is no external read and no current-eligibility read. An absent or foreign run or
/// step fails with the fixed <c>local_execution_ownership_release.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionOwnershipReleaseQuery(Guid RunId, Guid StepId)
    : IQuery<Result<GetLocalExecutionOwnershipReleaseQueryResult>>;
