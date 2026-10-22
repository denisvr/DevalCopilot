using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one allocator of the stable Int64 coordination and admission numbers (ADR-0062). It is called only under the database write lock
/// the caller already took, reloads the feature-owned counter so a competing writer that committed first is read, and never wraps,
/// reuses or coerces a malformed or exhausted counter: it then returns null and the caller fails closed. The advanced counter is saved
/// in the caller's own transaction, so a refused or rolled-back save hands out no number.
/// </summary>
internal static class LocalExecutionOrdering
{
    public static async Task<LocalExecutionOrderCounter?> ReadCounterAsync(IDevalCopilotDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            var counter = await dbContext.LocalExecutionOrderCounters
                .SingleOrDefaultAsync(candidate => candidate.Id == LocalExecutionOrderCounter.SingletonId, cancellationToken);
            if (counter is not null)
            {
                await dbContext.Entry(counter).ReloadAsync(cancellationToken);
            }

            return counter;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The next number, or null when the counter is missing, malformed or exhausted.</summary>
    public static long? Next(LocalExecutionOrderCounter? counter)
    {
        if (counter is null)
        {
            return null;
        }

        try
        {
            return counter.Allocate();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
