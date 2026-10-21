using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

/// <summary>
/// Answers <see cref="ILocalExecutionStepProbe.CheckAsync"/> through a brand-new <see cref="DevalCopilotDbContext"/>: its own
/// independent connection, never the claim's own context. A claim is <see cref="LocalExecutionStepProbeResult.Claimed"/> only when the
/// step is linked to its reserved operation identity and that operation row exists, and
/// <see cref="LocalExecutionStepProbeResult.NotClaimed"/> only when it is unlinked and no such operation row exists. Any other
/// combination, a failed read or an elapsed bound is unresolved. A genuine caller cancellation still propagates.
/// </summary>
public sealed class LocalExecutionStepProbe(DbContextOptions<DevalCopilotDbContext> options) : ILocalExecutionStepProbe
{
    private static readonly TimeSpan BoundedReadTimeout = TimeSpan.FromSeconds(5);

    public async Task<LocalExecutionStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken)
    {
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        boundedCts.CancelAfter(BoundedReadTimeout);

        try
        {
            await using var probeContext = new DevalCopilotDbContext(options);
            var step = await probeContext.LocalExecutionSequenceSteps
                .AsNoTracking()
                .Where(candidate => candidate.Id == stepId)
                .Select(candidate => new { candidate.State, candidate.OperationId, candidate.ReservedOperationId, candidate.Kind })
                .SingleOrDefaultAsync(boundedCts.Token);
            if (step is null)
            {
                return LocalExecutionStepProbeResult.Unresolved;
            }

            var operationExists = step.Kind == LocalExecutionStepKind.Verification
                ? await probeContext.VerificationExecutions.AsNoTracking()
                    .AnyAsync(candidate => candidate.Id == step.ReservedOperationId, boundedCts.Token)
                : await probeContext.Attempts.AsNoTracking()
                    .AnyAsync(candidate => candidate.Id == step.ReservedOperationId, boundedCts.Token);
            var linked = step.State is LocalExecutionSequenceStepState.Claimed or LocalExecutionSequenceStepState.Concluded;

            return linked && operationExists && step.OperationId == step.ReservedOperationId ? LocalExecutionStepProbeResult.Claimed
                : !linked && !operationExists && step.OperationId is null ? LocalExecutionStepProbeResult.NotClaimed
                : LocalExecutionStepProbeResult.Unresolved;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return LocalExecutionStepProbeResult.Unresolved;
        }
    }
}
