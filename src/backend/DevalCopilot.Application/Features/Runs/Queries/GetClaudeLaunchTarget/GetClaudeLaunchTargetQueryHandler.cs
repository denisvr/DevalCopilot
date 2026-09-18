using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;

public sealed class GetClaudeLaunchTargetQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetClaudeLaunchTargetQuery, ClaudeLaunchTarget?>
{
    public async Task<ClaudeLaunchTarget?> HandleAsync(GetClaudeLaunchTargetQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);

        // Fail closed, never fall back to treating a resolved script as usable here: Claude is
        // only ever accepted as a DirectExecutable (its shipped native claude.exe) — see
        // PackageEntrypointResolver's PE-header check. A snapshot observed with any other launch
        // kind, or with a script path, is never a launch target this query returns.
        if (snapshot is null
            || snapshot.ReasonCode != CapabilityProbeReason.None
            || snapshot.LaunchKind != CapabilityLaunchKind.DirectExecutable
            || !string.IsNullOrEmpty(snapshot.ResolvedScriptPath)
            || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
        {
            return null;
        }

        return new ClaudeLaunchTarget(snapshot.ResolvedExecutablePath);
    }
}
