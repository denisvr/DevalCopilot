using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;

public sealed class GetCodexLaunchTargetQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCodexLaunchTargetQuery, CodexLaunchTarget?>
{
    public async Task<CodexLaunchTarget?> HandleAsync(GetCodexLaunchTargetQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        if (snapshot is null || snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
        {
            return null;
        }

        return new CodexLaunchTarget(snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath);
    }
}
