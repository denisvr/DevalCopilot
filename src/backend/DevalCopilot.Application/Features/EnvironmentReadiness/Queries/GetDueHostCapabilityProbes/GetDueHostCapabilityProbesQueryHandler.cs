using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetDueHostCapabilityProbes;

public sealed class GetDueHostCapabilityProbesQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetDueHostCapabilityProbesQuery, IReadOnlyList<Capability>>
{
    public async Task<IReadOnlyList<Capability>> HandleAsync(
        GetDueHostCapabilityProbesQuery query, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        // ProbeDispatchedAtUtc == null (excluding any capability already claimed by an
        // in-flight probe this cycle) translates and filters server-side; the catalog is only
        // ever 7 rows, so the NextProbeDueAtUtc <= nowUtc comparison — DateTimeOffset comparison
        // does not reliably translate against SQLite, the same limitation already documented on
        // GetEligibleProcessAttemptsQueryHandler's ordering — is applied client-side instead.
        var undispatched = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProbeDispatchedAtUtc == null)
            .Select(snapshot => new { snapshot.Capability, snapshot.NextProbeDueAtUtc })
            .ToListAsync(cancellationToken);

        return undispatched
            .Where(snapshot => snapshot.NextProbeDueAtUtc <= nowUtc)
            .Select(snapshot => snapshot.Capability)
            .ToArray();
    }
}
