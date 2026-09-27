using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// Reads the same durable, already-vetted Codex launch target
/// <c>GetCodexLaunchTargetQueryHandler</c> reads (a small, deliberate duplication of that
/// five-line lookup — this operation owns its own read rather than depending on another
/// operation's result type) and, only when one currently resolves successfully, asks
/// <see cref="ICodexAccountAllowanceAdapter"/> for one fresh snapshot. No vetted target and no
/// adapter observation both collapse to the same explicit <see cref="CodexAccountAllowanceStatus.Unknown"/>
/// projection — this query never fails in an expected way.
/// </summary>
public sealed class GetCodexAccountAllowanceQueryHandler(IDevalCopilotDbContext dbContext, ICodexAccountAllowanceAdapter adapter)
    : IQueryHandler<GetCodexAccountAllowanceQuery, GetCodexAccountAllowanceQueryResult>
{
    public async Task<GetCodexAccountAllowanceQueryResult> HandleAsync(
        GetCodexAccountAllowanceQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        if (snapshot is null || snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
        {
            return GetCodexAccountAllowanceQueryResult.Unknown;
        }

        var observation = await adapter.ObserveAsync(snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath, cancellationToken);

        return observation.IsObserved
            ? new GetCodexAccountAllowanceQueryResult(CodexAccountAllowanceStatus.Observed, observation.RetrievedAtUtc, observation.Buckets)
            : GetCodexAccountAllowanceQueryResult.Unknown;
    }
}
