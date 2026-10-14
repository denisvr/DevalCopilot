using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// Requests one explicit observation of the bounded check and status contexts GitHub returns for the exact recorded commit of a run whose
/// repository identity is bound (ADR-0045). It carries no repository, node ID, branch, commit, cursor, limit, executable or credential: the
/// scope comes only from the recorded source, target and binding rows. It writes, locks and discovers nothing and grants no approval,
/// permission or publication authority.
/// </summary>
public sealed record GetGitHubCheckContextsObservationQuery(Guid RunId)
    : IQuery<Result<GetGitHubCheckContextsObservationQueryResult>>;
