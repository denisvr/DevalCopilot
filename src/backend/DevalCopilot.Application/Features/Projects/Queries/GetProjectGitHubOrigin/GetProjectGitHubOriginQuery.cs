using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// Requests one explicit observation of a registered project's repository-local GitHub origin (ADR-0039). It carries only
/// the project identity: never a path, a remote, a URL or any other client choice, and it discovers, rechecks and writes
/// nothing.
/// </summary>
public sealed record GetProjectGitHubOriginQuery(Guid ProjectId) : IQuery<Result<GetProjectGitHubOriginQueryResult>>;
