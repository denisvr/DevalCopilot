using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetTerminalAgentAttempts;

public sealed class GetTerminalAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetTerminalAgentAttemptsQuery, IReadOnlyList<TerminalAgentAttempt>>
{
    public async Task<IReadOnlyList<TerminalAgentAttempt>> HandleAsync(
        GetTerminalAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.Status != AttemptStatus.Running)
            .Select(attempt => new TerminalAgentAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
