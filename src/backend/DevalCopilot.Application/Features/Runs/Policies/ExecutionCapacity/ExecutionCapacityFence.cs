using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>
/// The additional veto every potentially mutating entrance shares (ADR-0058, with the limit of ADR-0059): a claim or admission may pass
/// only while fewer coherent owners than the current persisted limit are recorded, and a single-use dispatch or execution gate only while
/// fewer owners than that limit are recorded beside its exact operation. Capacity is never a grant: it runs after, and never replaces, the
/// workspace, lineage, mode, budget, source, approval and physical-ownership gates. A claim runs it once before its expensive preparation
/// and again after taking the existing short write lock, immediately before its commit; a dispatch gate runs it under its own write lock.
/// Each check reads the limit and the owners fresh and untracked, so a limit observed before preparation never authorizes a later claim.
/// It terminalizes nothing, cancels nothing, releases nothing and chooses no winner.
/// </summary>
internal static class ExecutionCapacityFence
{
    /// <summary>Refuses a new owner when as many coherent owners as the persisted limit are recorded, or when the limit or the recorded
    /// ownership is untrustworthy.</summary>
    public static async Task<Error?> CheckClaimAsync(
        IDevalCopilotDbContext dbContext, CancellationToken cancellationToken, LocalExecutionStepTicket? ownReservation = null) =>
        ToError(await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken, ownReservation), callerOwners: 0);

    /// <summary>Refuses dispatching exactly this owner when as many owners as the persisted limit are recorded beside it. The owner must itself be a coherent
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
        reading is not { RecordedOwners: { } recorded, Limit: { } limit } || reading.Status == ExecutionCapacityStatus.Unavailable
            ? ExecutionCapacityErrors.Unavailable()
            : recorded - callerOwners >= limit
                ? ExecutionCapacityErrors.LimitReached()
                : null;
}
