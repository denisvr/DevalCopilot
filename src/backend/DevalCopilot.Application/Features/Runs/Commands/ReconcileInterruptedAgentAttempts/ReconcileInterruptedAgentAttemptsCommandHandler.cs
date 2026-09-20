using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;

public sealed class ReconcileInterruptedAgentAttemptsCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ReconcileInterruptedAgentAttemptsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(ReconcileInterruptedAgentAttemptsCommand command, CancellationToken cancellationToken)
    {
        // A WorkspaceMutating-effect role's attempt is excluded: unlike every ReadOnly role, a
        // dispatched mutating attempt may have mutated the worktree before the host was lost, so
        // it is never safe to just mark it Interrupted here — ReconcileInterruptedImplementationAttemptsCommand
        // independently re-reads fresh Git evidence outside any EF transaction before deciding
        // whether the workspace must also be flagged NeedsAttention. The role set is derived from
        // AgentAttemptContract, not a hardcoded role literal, and materialized before the query so
        // EF Core translates the check to a closed SQL IN (...) predicate.
        var readOnlyRoles = AgentAttemptContract.RolesForEffect(AgentEffectKind.ReadOnly).ToArray();
        var interruptedAttempts = await dbContext.Attempts
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent && readOnlyRoles.Contains(attempt.AgentRole!.Value) && attempt.Status == AttemptStatus.Running)
            .ToListAsync(cancellationToken);

        if (interruptedAttempts.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var runIds = interruptedAttempts.Select(attempt => attempt.RunId).Distinct().ToArray();
        var runsById = await dbContext.Runs.Where(run => runIds.Contains(run.Id)).ToDictionaryAsync(run => run.Id, cancellationToken);

        // Validate every affected attempt/run pair before mutating anything — the same
        // never-partially-mutate rule ReconcileInterruptedProcessAttemptsCommandHandler applies.
        foreach (var attempt in interruptedAttempts)
        {
            if (!runsById.TryGetValue(attempt.RunId, out var run))
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.orphaned_run", $"Agent attempt {attempt.Id} has no owning run."));
            }

            if (run.Lifecycle != RunLifecycle.Running)
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.inconsistent_run_state", $"Agent attempt {attempt.Id} is Running but its run is {run.Lifecycle}, not Running."));
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
