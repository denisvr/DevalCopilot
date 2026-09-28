using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// Reads the same durable, already-vetted Codex launch target <c>GetCodexLaunchTargetQueryHandler</c>
/// reads (a small, deliberate duplication of that five-line lookup, mirroring
/// <c>GetCodexAccountAllowanceQueryHandler</c> — this operation owns its own read rather than
/// depending on another operation's result type) and, only when one currently resolves
/// successfully, asks <see cref="ICodexModelCatalogAdapter"/> for one fresh catalog observation.
/// No vetted target and no adapter observation both collapse to the same explicit
/// <see cref="CodexModelCatalogStatus.Unknown"/> projection — this query never fails in an
/// expected way.
/// </summary>
public sealed class GetCodexModelCatalogQueryHandler(IDevalCopilotDbContext dbContext, ICodexModelCatalogAdapter adapter)
    : IQueryHandler<GetCodexModelCatalogQuery, GetCodexModelCatalogQueryResult>
{
    public async Task<GetCodexModelCatalogQueryResult> HandleAsync(
        GetCodexModelCatalogQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        if (snapshot is null || snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
        {
            return GetCodexModelCatalogQueryResult.Unknown;
        }

        var observation = await adapter.ObserveAsync(snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath, cancellationToken);

        return observation.IsObserved
            ? new GetCodexModelCatalogQueryResult(CodexModelCatalogStatus.Observed, observation.RetrievedAtUtc, observation.Models)
            : GetCodexModelCatalogQueryResult.Unknown;
    }
}
