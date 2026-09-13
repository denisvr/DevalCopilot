using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;

public sealed class EnsureHostCapabilityCatalogSeededCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<EnsureHostCapabilityCatalogSeededCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        EnsureHostCapabilityCatalogSeededCommand command, CancellationToken cancellationToken)
    {
        var existingCapabilities = await dbContext.HostCapabilitySnapshots
            .Select(snapshot => snapshot.Capability)
            .ToListAsync(cancellationToken);

        var missingCapabilities = CapabilityCatalog.All.Except(existingCapabilities).ToArray();
        if (missingCapabilities.Length == 0)
        {
            return Result<int>.Success(0);
        }

        var nowUtc = timeProvider.GetUtcNow();
        foreach (var capability in missingCapabilities)
        {
            dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(capability, nowUtc));
        }

        return Result<int>.Success(missingCapabilities.Length);
    }
}
