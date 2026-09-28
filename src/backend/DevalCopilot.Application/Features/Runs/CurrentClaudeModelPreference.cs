using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Shared claim-time snapshot and guard for the Run's own current, explicit Claude model-alias
/// request, kept in one place so the CriticalReviewer, Implementer, and ReviewCorrection claim
/// handlers read and guard it identically.
///
/// <para>
/// Each claim handler loads its tracked <c>Run</c> at the start of the request, long before its
/// external Git evidence capture and manifest sealing. Reading that instance's
/// <c>RequestedClaudeModel</c> late would return whatever was current at that early load.
/// <see cref="ReadAndGuardAsync"/> instead reads the column afresh, untracked, as late as possible
/// (after all external work, immediately before the Attempt is constructed), and then marks the
/// tracked Run's <c>RequestedClaudeModel</c> as modified with that fresh value as its original
/// value. The column is an EF concurrency token (see <c>RunConfiguration</c>), so the claim's own
/// single <c>SaveChangesAsync</c> emits an <c>UPDATE runs ... WHERE Id, Lifecycle, RequestedClaudeModel</c>
/// requiring the exact value the Attempt snapshotted. A preference change committed after the read
/// makes that UPDATE match zero rows and the whole batch — the Attempt insert included — rolls back
/// with <see cref="DbUpdateConcurrencyException"/>, so an Attempt can never durably commit a
/// snapshot that was no longer current at commit. SQLite's write lock makes the compare-and-insert
/// one serialized unit; no external I/O happens between the read and the save.
/// </para>
/// </summary>
public static class CurrentClaudeModelPreference
{
    public const string RunChangedDuringClaimCode = "agent_attempts.run_changed_during_claim";

    public static async Task<string?> ReadAndGuardAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var current = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => candidate.RequestedClaudeModel)
            .SingleAsync(cancellationToken);

        var property = dbContext.Entry(run).Property(candidate => candidate.RequestedClaudeModel);
        property.OriginalValue = current;
        property.CurrentValue = current;
        property.IsModified = true;

        return current;
    }

    /// <summary>The run's lifecycle or Claude model request changed between this claim's read and
    /// its commit; nothing from the claim persisted, and retrying re-reads the current state.</summary>
    public static Error RunChangedDuringClaim() => Error.Conflict(
        RunChangedDuringClaimCode,
        "The run changed while this attempt was being claimed; retry the request.");
}
