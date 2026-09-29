using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Shared claim-time snapshot and guard for the Run's own current, explicit Claude model-alias and
/// effort request, kept in one place so the CriticalReviewer, Implementer, and ReviewCorrection
/// claim handlers read and guard the pair identically.
///
/// <para>
/// Each claim handler loads its tracked <c>Run</c> at the start of the request, long before its
/// external Git evidence capture and manifest sealing. Reading that instance's
/// <c>RequestedClaudeModel</c> or <c>RequestedClaudeEffort</c> late would return whatever was
/// current at that early load. <see cref="ReadAndGuardAsync"/> instead reads both columns afresh,
/// untracked and in one query, as late as possible (after all external work, immediately before
/// the Attempt is constructed), and then marks the tracked Run's two properties as modified with
/// those fresh values as their original values. Both columns are EF concurrency tokens (see
/// <c>RunConfiguration</c>), so the claim's own single <c>SaveChangesAsync</c> emits an
/// <c>UPDATE runs ... WHERE Id, Lifecycle, RequestedClaudeModel, RequestedClaudeEffort</c>
/// requiring the exact pair the Attempt snapshotted. A preference change committed after the read
/// makes that UPDATE match zero rows and the whole batch — the Attempt insert included — rolls back
/// with <see cref="DbUpdateConcurrencyException"/>, so an Attempt can never durably commit a
/// snapshot that was no longer current at commit. SQLite's write lock makes the compare-and-insert
/// one serialized unit; no external I/O happens between the read and the save.
/// </para>
/// </summary>
public static class CurrentClaudeModelPreference
{
    public const string RunChangedDuringClaimCode = "agent_attempts.run_changed_during_claim";

    public static async Task<ClaudeModelRequestSnapshot> ReadAndGuardAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var current = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => new ClaudeModelRequestSnapshot(candidate.RequestedClaudeModel, candidate.RequestedClaudeEffort))
            .SingleAsync(cancellationToken);

        var model = dbContext.Entry(run).Property(candidate => candidate.RequestedClaudeModel);
        model.OriginalValue = current.Model;
        model.CurrentValue = current.Model;
        model.IsModified = true;

        var effort = dbContext.Entry(run).Property(candidate => candidate.RequestedClaudeEffort);
        effort.OriginalValue = current.Effort;
        effort.CurrentValue = current.Effort;
        effort.IsModified = true;

        return current;
    }

    /// <summary>The run's lifecycle or Claude model/effort request changed between this claim's read
    /// and its commit; nothing from the claim persisted, and retrying re-reads the current state.</summary>
    public static Error RunChangedDuringClaim() => Error.Conflict(
        RunChangedDuringClaimCode,
        "The run changed while this attempt was being claimed; retry the request.");
}
