using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;

public sealed record GetProjectWorkspaceQuery(Guid ProjectId) : IQuery<Result<GetProjectWorkspaceQueryResult>>;
