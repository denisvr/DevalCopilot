using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationExecutions;

public sealed record GetProjectVerificationExecutionsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<VerificationExecutionQueryResult>>;
