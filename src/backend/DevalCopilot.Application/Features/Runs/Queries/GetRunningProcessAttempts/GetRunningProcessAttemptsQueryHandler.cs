using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunningProcessAttempts;

public sealed class GetRunningProcessAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetRunningProcessAttemptsQuery, IReadOnlyList<RunningProcessAttempt>>
{
    public async Task<IReadOnlyList<RunningProcessAttempt>> HandleAsync(
        GetRunningProcessAttemptsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Process && attempt.Status == AttemptStatus.Running)
            .Select(attempt => new RunningProcessAttempt(attempt.RunId, attempt.Id))
            .ToListAsync(cancellationToken);
    }
}
