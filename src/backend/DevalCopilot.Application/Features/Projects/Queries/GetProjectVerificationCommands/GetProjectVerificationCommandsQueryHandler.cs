using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationCommands;

public sealed class GetProjectVerificationCommandsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectVerificationCommandsQuery, IReadOnlyList<VerificationCommandQueryResult>>
{
    public async Task<IReadOnlyList<VerificationCommandQueryResult>> HandleAsync(
        GetProjectVerificationCommandsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.VerificationCommands
            .AsNoTracking()
            .Where(command => command.ProjectId == query.ProjectId)
            .OrderBy(command => command.CommandNumber)
            .Select(command => new VerificationCommandQueryResult(
                command.Id,
                command.CommandNumber,
                command.Name,
                command.ExecutablePath,
                command.Arguments.ToList(),
                command.TimeoutSeconds,
                command.IsEnabled,
                command.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
