using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>
/// Requests one explicit observation of the GitHub repository identity (ID and names) of a run's recorded ADR-0040 target. It carries
/// no owner, URL, branch, commit, executable or credential: the scope comes only from the recorded rows. It writes, locks and
/// discovers nothing, and proves no remote branch, commit, permission, CI result, publication approval or binding (ADR-0041).
/// </summary>
public sealed record GetGitHubRepositoryObservationQuery(Guid RunId) : IQuery<Result<GetGitHubRepositoryObservationQueryResult>>;
