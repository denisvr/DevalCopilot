using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Read-side access to persisted Agent attempts for the history, evidence, and sealed-window queries.
/// EF converts the status, role, provider, and contract columns from strings while materializing an
/// <see cref="Attempt"/>, so a corrupted or legacy string throws before
/// <see cref="AgentAttemptIdentity.IsCoherent"/> can run. These helpers first read only non-enum
/// columns (so existence, run ownership, ordering, and paging never depend on the corrupt value) and
/// then materialize the full row inside a narrow guard that catches any <see cref="InvalidOperationException"/>
/// raised while materializing that row. It cannot prove the exception came specifically from enum
/// conversion, so an unreadable row (null) means only "this row could not be materialized":
/// callers report it as an unverifiable identity with a fixed message — never the exception,
/// the stored string, or any artifact content. Database and cancellation failures are not caught.
/// </summary>
public static class AgentAttemptRead
{
    public static Task<AgentAttemptScalars?> FindScalarsAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid attemptId, CancellationToken cancellationToken) =>
        dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Id == attemptId && attempt.RunId == runId && attempt.Kind == AttemptKind.Agent)
            .Select(attempt => new AgentAttemptScalars(
                attempt.Id, attempt.AttemptNumber, attempt.ClaimedAtUtc, attempt.CompletedAtUtc, attempt.AgentDispatchedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Materializes one attempt, or returns null when materialization throws an
    /// <see cref="InvalidOperationException"/> (typically an unconvertible persisted enum string, though the
    /// exception type alone cannot prove that).</summary>
    public static async Task<Attempt?> TryMaterializeAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
