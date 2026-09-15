using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationCommands;

public sealed record GetProjectVerificationCommandsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<VerificationCommandQueryResult>>;
