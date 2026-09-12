using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleSimulatedRuns;

public sealed class GetEligibleSimulatedRunsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleSimulatedRunsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(
        GetEligibleSimulatedRunsQuery query,
        CancellationToken cancellationToken)
    {
        // SQLite cannot translate ORDER BY over DateTimeOffset server-side, and the
        // eligible set is always small (bounded by the configured concurrency limit),
        // so oldest-first ordering happens client-side after a narrow projection.
        var eligibleRuns = await dbContext.Runs
            .AsNoTracking()
            .Where(run => run.Lifecycle == RunLifecycle.Created)
            .Select(run => new { run.Id, run.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return eligibleRuns
            .OrderBy(run => run.CreatedAtUtc)
            .Select(run => run.Id)
            .ToArray();
    }
}
