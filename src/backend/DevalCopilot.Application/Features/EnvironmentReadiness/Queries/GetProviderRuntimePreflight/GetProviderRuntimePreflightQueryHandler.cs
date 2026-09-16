using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

public sealed class GetProviderRuntimePreflightQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetProviderRuntimePreflightQuery, IReadOnlyList<ProviderRuntimePreflightQueryResult>>
{
    public async Task<IReadOnlyList<ProviderRuntimePreflightQueryResult>> HandleAsync(
        GetProviderRuntimePreflightQuery query, CancellationToken cancellationToken)
    {
        var snapshots = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return ProviderRuntimePreflightProjector.Project(
            HostCapabilityReadinessProjector.Project(snapshots, timeProvider.GetUtcNow()));
    }
}
