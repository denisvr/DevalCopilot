using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// Requests one explicit observation of the GitHub repository identity and the exact recorded branch tip of a run's recorded ADR-0040
/// target. It carries no repository, node ID, branch, commit, executable or credential: the scope comes only from the recorded rows. It
/// writes, locks and discovers nothing, and proves no CI result, permission, durable binding or publication approval (ADR-0042).
/// </summary>
public sealed record GetGitHubBranchObservationQuery(Guid RunId) : IQuery<Result<GetGitHubBranchObservationQueryResult>>;
