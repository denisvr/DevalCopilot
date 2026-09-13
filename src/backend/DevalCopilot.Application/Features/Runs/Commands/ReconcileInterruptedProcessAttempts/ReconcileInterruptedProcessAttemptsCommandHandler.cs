using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;

public sealed class ReconcileInterruptedProcessAttemptsCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ReconcileInterruptedProcessAttemptsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        ReconcileInterruptedProcessAttemptsCommand command,
        CancellationToken cancellationToken)
    {
        var interruptedAttempts = await dbContext.Attempts
            .Where(attempt => attempt.Kind == AttemptKind.Process && attempt.Status == AttemptStatus.Running)
            .ToListAsync(cancellationToken);

        if (interruptedAttempts.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var runIds = interruptedAttempts.Select(attempt => attempt.RunId).Distinct().ToArray();
        var runsById = await dbContext.Runs
            .Where(run => runIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, cancellationToken);

        // Validate every affected attempt/run pair before mutating anything: a Running
        // Process attempt whose run is missing or no longer Running is an inconsistent state
        // reconciliation must never paper over by silently interrupting only the attempt —
        // it fails the whole reconciliation pass instead, with no partial mutation.
        foreach (var attempt in interruptedAttempts)
        {
            if (!runsById.TryGetValue(attempt.RunId, out var run))
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.orphaned_run",
                    $"Process attempt {attempt.Id} has no owning run."));
            }

            if (run.Lifecycle != RunLifecycle.Running)
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.inconsistent_run_state",
                    $"Process attempt {attempt.Id} is Running but its run is {run.Lifecycle}, not Running."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();
        foreach (var attempt in interruptedAttempts)
        {
            attempt.Interrupt(nowUtc);
            runsById[attempt.RunId].MarkInterrupted(nowUtc);
        }

        return Result<int>.Success(interruptedAttempts.Count);
    }
}
