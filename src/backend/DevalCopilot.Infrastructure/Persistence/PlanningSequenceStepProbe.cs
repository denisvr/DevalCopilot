using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

/// <summary>
/// Answers <see cref="IPlanningSequenceStepProbe.CheckAsync"/> through a brand-new <see cref="DevalCopilotDbContext"/>: its own
/// independent connection, never the claim's own context. The exact step/attempt association is read, not inferred: a claim is
/// <see cref="PlanningSequenceStepProbeResult.Claimed"/> only when the step is linked to its reserved attempt identity and that attempt
/// row exists, and <see cref="PlanningSequenceStepProbeResult.NotClaimed"/> only when it is unlinked and no such attempt row exists.
/// Any other combination, a failed read or an elapsed bound is unresolved. A genuine caller cancellation still propagates.
/// </summary>
public sealed class PlanningSequenceStepProbe(DbContextOptions<DevalCopilotDbContext> options) : IPlanningSequenceStepProbe
{
    private static readonly TimeSpan BoundedReadTimeout = TimeSpan.FromSeconds(5);

    public async Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken)
    {
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        boundedCts.CancelAfter(BoundedReadTimeout);

        try
        {
            await using var probeContext = new DevalCopilotDbContext(options);
            var step = await probeContext.PlanningSequenceSteps
                .AsNoTracking()
                .Where(candidate => candidate.Id == stepId)
                .Select(candidate => new { candidate.State, candidate.AttemptId, candidate.ReservedAttemptId })
                .SingleOrDefaultAsync(boundedCts.Token);
            if (step is null)
            {
                return PlanningSequenceStepProbeResult.Unresolved;
            }

            var attemptExists = await probeContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == step.ReservedAttemptId, boundedCts.Token);
            var linked = step.State is PlanningSequenceStepState.Claimed or PlanningSequenceStepState.Concluded;

            return linked && attemptExists && step.AttemptId == step.ReservedAttemptId ? PlanningSequenceStepProbeResult.Claimed
                : !linked && !attemptExists && step.AttemptId is null ? PlanningSequenceStepProbeResult.NotClaimed
                : PlanningSequenceStepProbeResult.Unresolved;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PlanningSequenceStepProbeResult.Unresolved;
        }
    }
}
