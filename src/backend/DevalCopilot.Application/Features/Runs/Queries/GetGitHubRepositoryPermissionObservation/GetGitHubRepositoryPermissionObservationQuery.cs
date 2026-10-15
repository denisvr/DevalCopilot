using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// Requests one explicit observation of the repository permission GitHub reports for the stored default authentication on the repository
/// of a run whose identity is bound (ADR-0049). It carries no repository, node ID, branch, commit, executable or credential: the scope
/// comes only from the recorded source, target and binding rows. It writes, locks and discovers nothing and grants no approval, push
/// permission or publication authority.
/// </summary>
public sealed record GetGitHubRepositoryPermissionObservationQuery(Guid RunId)
    : IQuery<Result<GetGitHubRepositoryPermissionObservationQueryResult>>;
