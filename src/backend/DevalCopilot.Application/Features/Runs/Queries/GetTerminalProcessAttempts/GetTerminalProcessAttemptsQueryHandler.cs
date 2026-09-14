using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetTerminalProcessAttempts;

public sealed class GetTerminalProcessAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetTerminalProcessAttemptsQuery, IReadOnlyList<TerminalProcessAttempt>>
{
    public async Task<IReadOnlyList<TerminalProcessAttempt>> HandleAsync(
        GetTerminalProcessAttemptsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Process && attempt.Status != AttemptStatus.Running)
            .Select(attempt => new TerminalProcessAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
