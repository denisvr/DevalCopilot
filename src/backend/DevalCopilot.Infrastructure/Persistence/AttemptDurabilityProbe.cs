using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

/// <summary>
/// Answers <see cref="IAttemptDurabilityProbe.CheckAsync"/> through a brand-new
/// <see cref="DevalCopilotDbContext"/> — its own independent connection, never the claim's own
/// <c>IDevalCopilotDbContext</c> instance — so a stuck or ambiguously-released transaction on that
/// original connection cannot make this read hang or silently observe a stale, in-transaction view.
/// The read is bounded by <see cref="BoundedReadTimeout"/>; a genuine caller cancellation
/// (<paramref name="cancellationToken"/> already signalled) still propagates rather than being
/// folded into <see cref="AttemptDurabilityCheckResult.Unresolved"/>.
/// </summary>
public sealed class AttemptDurabilityProbe(DbContextOptions<DevalCopilotDbContext> options) : IAttemptDurabilityProbe
{
    private static readonly TimeSpan BoundedReadTimeout = TimeSpan.FromSeconds(5);

    public async Task<AttemptDurabilityCheckResult> CheckAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        boundedCts.CancelAfter(BoundedReadTimeout);

        try
        {
            await using var probeContext = new DevalCopilotDbContext(options);
            var persisted = await probeContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, boundedCts.Token);
            return persisted ? AttemptDurabilityCheckResult.Persisted : AttemptDurabilityCheckResult.NotPersisted;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Either this probe's own bounded timeout fired, or the independent connection/read
            // itself failed (e.g. the same lock contention that made the original outcome
            // ambiguous is still in effect). Neither is a definite answer.
            return AttemptDurabilityCheckResult.Unresolved;
        }
    }
}
