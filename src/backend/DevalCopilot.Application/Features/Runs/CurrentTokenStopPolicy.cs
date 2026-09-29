using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The commit-time guard that keeps an Agent claim from durably committing against a stale token
/// stop policy. The policy is the pair of stop thresholds exactly as the claim's tracked
/// <c>Run</c> loaded them, which is what its <see cref="AgentTokenStopGate"/> decision used. Both
/// stop columns are EF concurrency tokens, and the two claim families use them differently:
///
/// <list type="bullet">
/// <item><description>Claude paths (one <c>SaveChangesAsync</c>): <see cref="Guard"/> marks both
/// columns modified, so the claim's own <c>UPDATE runs ... WHERE</c> requires the exact loaded pair
/// and a threshold change committed since then makes it match zero rows and roll the whole batch,
/// Attempt included, back.</description></item>
/// <item><description>Codex paths (explicit short transaction): <see cref="ConfirmUnchangedAsync"/>
/// is one atomic <c>UPDATE ... WHERE</c> compare executed inside that transaction before any
/// insert, so it takes the database write lock; the compare and the Attempt/artifact/input writes
/// are then one serialized unit.</description></item>
/// </list>
///
/// A threshold changed after a claim commits is prospective and never revokes that claimed attempt.
/// </summary>
public static class CurrentTokenStopPolicy
{
    public const string PolicyChangedDuringClaimCode = "agent_attempts.token_stop_policy_changed";

    /// <summary>Marks the tracked Run's stop columns modified with their loaded values, so the
    /// claim's <c>SaveChangesAsync</c> UPDATE guards them through their concurrency-token WHERE.</summary>
    public static void Guard(IDevalCopilotDbContext dbContext, Run run)
    {
        var entry = dbContext.Entry(run);
        entry.Property(candidate => candidate.CodexTokenStopThreshold).IsModified = true;
        entry.Property(candidate => candidate.ClaudeTokenStopThreshold).IsModified = true;
    }

    /// <summary>Atomically confirms, via one <c>ExecuteUpdateAsync</c> whose WHERE requires an exact
    /// match on both columns, that the stored stop policy is still the one the tracked Run loaded.
    /// The self-referential SET makes a match a no-op write. <see langword="false"/> means zero rows
    /// matched: the policy changed and the caller must not commit its claim.</summary>
    public static async Task<bool> ConfirmUnchangedAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var runId = run.Id;
        var codex = run.CodexTokenStopThreshold;
        var claude = run.ClaudeTokenStopThreshold;
        var affectedRowCount = await dbContext.Runs
            .Where(candidate => candidate.Id == runId
                && candidate.CodexTokenStopThreshold == codex
                && candidate.ClaudeTokenStopThreshold == claude)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.CodexTokenStopThreshold, candidate => candidate.CodexTokenStopThreshold),
                cancellationToken);

        return affectedRowCount == 1;
    }

    /// <summary>After a concurrency failure of a Claude claim's commit: reads the stored stop policy
    /// afresh, untracked, and reports whether it differs from the one the tracked Run loaded.</summary>
    public static async Task<bool> HasChangedAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var runId = run.Id;
        var current = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new { candidate.CodexTokenStopThreshold, candidate.ClaudeTokenStopThreshold })
            .SingleOrDefaultAsync(cancellationToken);

        return current is null
            || current.CodexTokenStopThreshold != run.CodexTokenStopThreshold
            || current.ClaudeTokenStopThreshold != run.ClaudeTokenStopThreshold;
    }

    /// <summary>The run's token stop policy changed between this claim's decision and its commit;
    /// nothing from the claim persisted, and retrying re-decides against the current policy.</summary>
    public static Error PolicyChangedDuringClaim() => Error.Conflict(
        PolicyChangedDuringClaimCode,
        "The run's token stop policy changed while this attempt was being claimed; retry the request.");
}
