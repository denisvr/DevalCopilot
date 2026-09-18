using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;

public sealed class GetRunningAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunningAgentAttemptsQuery, IReadOnlyList<RunningAgentAttempt>>
{
    public async Task<IReadOnlyList<RunningAgentAttempt>> HandleAsync(
        GetRunningAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.Status == AttemptStatus.Running)
            .Select(attempt => new RunningAgentAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
