using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Shared read-side query and commit-time guard for the Run's own current, explicit Codex
/// model/effort preference, kept in exactly one place so every Codex claim handler reads and
/// guards it identically, at the identical claim-time boundary — never a fourth place a future
/// change to this rule could silently drift from the other three.
///
/// <para>
/// Every claim handler loads its own tracked <c>Run</c> once, at the very start of the request,
/// long before the external Git evidence capture and artifact-sealing work that follows. Reading
/// that same tracked instance's <c>RequestedCodexModel</c>/<c>RequestedCodexEffort</c> late in the
/// handler would still return whatever value was current at that early load — stale if the owner
/// changed the preference anywhere during the external work in between. <see cref="ReadAsync"/>
/// instead issues a genuinely fresh, untracked query for only these two columns, intended to be
/// called as late as possible — after all external work completes and immediately before the
/// claimed <c>Attempt</c> is constructed from its result.
/// </para>
///
/// <para>
/// That read alone does not guard the narrower gap between itself and the claim's own durable
/// commit — nothing about a plain read prevents a different, wholly separate transaction from
/// committing a new preference in that gap, and <c>Run.Lifecycle</c> (already a concurrency token
/// for its own, unrelated race) guards nothing here either, since these two columns are never
/// themselves part of that check. <see cref="ConfirmUnchangedAsync"/> closes this gap without an
/// explicit multi-statement transaction: it issues one atomic <c>UPDATE ... WHERE</c> statement
/// requiring the Run's current values to still exactly match what <see cref="ReadAsync"/> returned,
/// executed as the very last statement before the claim's own <c>SaveChangesAsync</c> — the
/// database itself, not client-side timing, is what makes the check-and-compare atomic. When it
/// returns <see langword="false"/>, a concurrent preference-only change won that gap and the caller
/// must fail its claim safely (no Attempt insert, and any already-sealed artifact file cleaned up)
/// rather than durably commit an Attempt built from a pair that is no longer current.
/// </para>
/// </summary>
public static class CurrentCodexAssignmentPreference
{
    public static async Task<(string? RequestedModel, string? RequestedEffort)> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var current = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new { candidate.RequestedCodexModel, candidate.RequestedCodexEffort })
            .SingleAsync(cancellationToken);

        return (current.RequestedCodexModel, current.RequestedCodexEffort);
    }

    /// <summary>
    /// Atomically confirms, via one <c>ExecuteUpdateAsync</c> statement whose <c>WHERE</c> clause
    /// requires an exact match on both columns, that the Run's requested Codex model/effort pair is
    /// still exactly the pair an earlier <see cref="ReadAsync"/> call observed. The statement's own
    /// self-referential <c>SET</c> makes it a no-op write whenever the row does match — its only
    /// purpose is the atomic, database-enforced compare, never an actual data change. A return of
    /// <see langword="false"/> means zero rows matched: the pair changed after that read, and the
    /// caller must not proceed to durably claim an Attempt built from it.
    /// </summary>
    public static async Task<bool> ConfirmUnchangedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, string? requestedModel, string? requestedEffort, CancellationToken cancellationToken)
    {
        var affectedRowCount = await dbContext.Runs
            .Where(candidate => candidate.Id == runId
                && candidate.RequestedCodexModel == requestedModel
                && candidate.RequestedCodexEffort == requestedEffort)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.RequestedCodexModel, candidate => candidate.RequestedCodexModel),
                cancellationToken);

        return affectedRowCount == 1;
    }
}
