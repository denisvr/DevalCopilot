using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>
/// Requests one explicit observation of GitHub's point-in-time check summary for the exact recorded commit of a run whose repository identity
/// is bound (ADR-0044). It carries no repository, node ID, branch, commit, executable or credential: the scope comes only from the recorded
/// source, target and binding rows. It writes, locks and discovers nothing and grants no approval, permission or publication authority.
/// </summary>
public sealed record GetGitHubCiObservationQuery(Guid RunId) : IQuery<Result<GetGitHubCiObservationQueryResult>>;
