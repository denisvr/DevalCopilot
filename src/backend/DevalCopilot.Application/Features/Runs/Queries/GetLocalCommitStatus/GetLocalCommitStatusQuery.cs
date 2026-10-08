using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitStatus;

/// <summary>Read-only eligibility and operation status of a run's explicit local commit. An eligible status is advisory: the
/// command decides again from fresh authority.</summary>
public sealed record GetLocalCommitStatusQuery(Guid RunId) : IQuery<Result<GetLocalCommitStatusQueryResult>>;
