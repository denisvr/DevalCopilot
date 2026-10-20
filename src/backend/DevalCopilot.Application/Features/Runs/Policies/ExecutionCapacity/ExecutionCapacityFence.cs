using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>
/// The additional veto every potentially mutating entrance shares (ADR-0058): a claim or admission may pass only while at most one
/// coherent owner is recorded, and a single-use dispatch or execution gate only while no more than one owner other than its exact
/// operation is recorded. Capacity is never a grant: it runs after, and never replaces, the workspace, lineage, mode, budget, source,
/// approval and physical-ownership gates. A claim runs it once before its expensive preparation and again after taking the existing short
/// write lock, immediately before its commit; a dispatch gate runs it under its own write lock. Each check is a fresh, untracked read.
/// It terminalizes nothing, cancels nothing, releases nothing and chooses no winner.
/// </summary>
internal static class ExecutionCapacityFence
{
    /// <summary>Refuses a new owner when two or more coherent owners are recorded, or when the recorded ownership is untrustworthy.</summary>
    public static async Task<Error?> CheckClaimAsync(IDevalCopilotDbContext dbContext, CancellationToken cancellationToken) =>
        ToError(await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken), callerOwners: 0);

    /// <summary>Refuses dispatching exactly this owner when two owners other than it are recorded. The owner must itself be a coherent
    /// counted owner of its own kind; another table's identifier is never excluded.</summary>
    public static async Task<Error?> CheckDispatchAsync(
        IDevalCopilotDbContext dbContext, ExecutionOwner owner, CancellationToken cancellationToken) =>
        ToError(await ExecutionCapacityPolicy.ReadAsync(dbContext, owner, cancellationToken), callerOwners: 1);

    /// <summary>The Agent dispatch gate: only an attempt Domain classifies WorkspaceMutating consumes capacity, so a recognized ReadOnly
    /// attempt passes unchanged while any other pair is judged (and refused as unavailable).</summary>
    public static Task<Error?> CheckAgentDispatchAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, CancellationToken cancellationToken) =>
        ExecutionCapacityPolicy.IsReadOnly(attempt.AgentResponseContract, attempt.AgentRole)
            ? Task.FromResult<Error?>(null)
            : CheckDispatchAsync(dbContext, new ExecutionOwner(ExecutionOwnerKind.AgentAttempt, attempt.Id), cancellationToken);

    /// <summary>The stable refusal for one reading, or null when it admits the caller. <paramref name="callerOwners"/> is how many of the
    /// counted owners are the caller's own (none for a claim, exactly one for a dispatch), so only the owners beside it count against it.</summary>
    private static Error? ToError(ExecutionCapacityReading reading, int callerOwners) =>
        reading.RecordedOwners is not { } recorded || reading.Status == ExecutionCapacityStatus.Unavailable
            ? ExecutionCapacityErrors.Unavailable()
            : recorded - callerOwners >= ExecutionCapacityPolicy.Limit
                ? ExecutionCapacityErrors.LimitReached()
                : null;
}
