using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.ReconcileInterruptedHostCapabilityProbes;

public sealed class ReconcileInterruptedHostCapabilityProbesCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ReconcileInterruptedHostCapabilityProbesCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        ReconcileInterruptedHostCapabilityProbesCommand command, CancellationToken cancellationToken)
    {
        var stuckSnapshots = await dbContext.HostCapabilitySnapshots
            .Where(snapshot => snapshot.ProbeDispatchedAtUtc != null)
            .ToListAsync(cancellationToken);

        if (stuckSnapshots.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var nowUtc = timeProvider.GetUtcNow();
        foreach (var snapshot in stuckSnapshots)
        {
            snapshot.ClearStuckDispatch(nowUtc);
        }

        return Result<int>.Success(stuckSnapshots.Count);
    }
}
