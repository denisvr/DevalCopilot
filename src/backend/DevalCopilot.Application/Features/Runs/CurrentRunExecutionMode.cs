using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Fresh-authority reads and commit-time guards for a Run's immutable <see cref="RunExecutionMode"/>.
/// A tracked <c>Run</c> may be long-lived and stale, so no decision is taken from its loaded mode: every check reads the
/// stored value afresh and untracked, as its exact stored form (<see cref="RunExecutionModeStorage"/>), never through an
/// integer conversion. Only the exact integers 0, 1, and 2 are recognized; a REAL, an out-of-range or oversized integer,
/// text, or a BLOB is refused by every check without being coerced or thrown on. The mode is an EF concurrency token, and
/// the two claim families guard it differently, exactly like <see cref="CurrentTokenStopPolicy"/>:
///
/// <list type="bullet">
/// <item><description>Claude paths (one <c>SaveChangesAsync</c>): <see cref="ReadAndGuardAgentAsync"/> reads the stored
/// form afresh, refuses an unadmitted one, and marks the tracked Run's mode modified with that value, so the claim's own
/// <c>UPDATE runs ... WHERE</c> requires the exact stored value it decided against.</description></item>
/// <item><description>Codex paths (explicit short transaction): <see cref="ConfirmAgentAdmittedAsync"/> is one atomic
/// <c>UPDATE ... WHERE</c> inside that transaction, before any insert, requiring an admitted stored form.</description></item>
/// </list>
/// </summary>
public static class CurrentRunExecutionMode
{
    public const string NotAdmittedCode = "runs.execution_mode_not_admitted";
    public const string ChangedDuringClaimCode = "runs.execution_mode_changed_during_claim";

    public static Error NotAdmitted() => Error.Conflict(
        NotAdmittedCode,
        "This run's execution mode does not permit this operation.");

    public static Error ChangedDuringClaim() => Error.Conflict(
        ChangedDuringClaimCode,
        "The run's execution mode changed while this operation was being committed; retry the request.");

    /// <summary>The exact stored form, read afresh and untracked; <see langword="null"/> when the run is missing.</summary>
    public static async Task<string?> ReadStoredAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => EF.Property<string>(candidate, Run.ExecutionModeStorageProperty))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The recognized mode currently stored, or <see cref="RunExecutionModeStorage.Unrecognized"/>.</summary>
    public static async Task<RunExecutionMode> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        RunExecutionModeStorage.Read(await ReadStoredAsync(dbContext, runId, cancellationToken));

    /// <summary>Fresh check for an Agent claim path: null when the stored mode admits Agent work.</summary>
    public static async Task<Error?> CheckAgentAdmittedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        RunExecutionModeAdmission.AdmitsAgent(await ReadAsync(dbContext, runId, cancellationToken)) ? null : NotAdmitted();

    /// <summary>Fresh check for a simulation path: null when the stored mode admits simulation.</summary>
    public static async Task<Error?> CheckSimulationAdmittedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        RunExecutionModeAdmission.AdmitsSimulation(await ReadAsync(dbContext, runId, cancellationToken)) ? null : NotAdmitted();

    /// <summary>Fresh check for a standalone Process path: null when the stored mode admits Process work.</summary>
    public static async Task<Error?> CheckProcessAdmittedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        RunExecutionModeAdmission.AdmitsProcess(await ReadAsync(dbContext, runId, cancellationToken)) ? null : NotAdmitted();

    /// <summary>Reads the stored form afresh; when it admits Agent work, marks the tracked Run's mode modified with that
    /// value so the claim's commit requires it unchanged, and returns null. Otherwise returns the refusal and guards
    /// nothing.</summary>
    public static Task<Error?> ReadAndGuardAgentAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken) =>
        ReadAndGuardAsync(dbContext, run, RunExecutionModeAdmission.AdmitsAgent, cancellationToken);

    /// <summary>The same fresh read and commit guard for a simulation path.</summary>
    public static Task<Error?> ReadAndGuardSimulationAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken) =>
        ReadAndGuardAsync(dbContext, run, RunExecutionModeAdmission.AdmitsSimulation, cancellationToken);

    /// <summary>The same fresh read and commit guard for a standalone Process path.</summary>
    public static Task<Error?> ReadAndGuardProcessAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken) =>
        ReadAndGuardAsync(dbContext, run, RunExecutionModeAdmission.AdmitsProcess, cancellationToken);

    private static async Task<Error?> ReadAndGuardAsync(
        IDevalCopilotDbContext dbContext, Run run, Func<RunExecutionMode, bool> admits, CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(dbContext, run.Id, cancellationToken);
        if (!admits(RunExecutionModeStorage.Read(stored)))
        {
            return NotAdmitted();
        }

        var property = dbContext.Entry(run).Property<string>(Run.ExecutionModeStorageProperty);
        property.OriginalValue = stored!;
        property.CurrentValue = stored!;
        property.IsModified = true;

        return null;
    }

    /// <summary>After a concurrency failure of a Claude claim's commit: whether the stored form differs from the one the
    /// claim decided against.</summary>
    public static async Task<bool> HasChangedAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var decidedAgainst = dbContext.Entry(run).Property<string>(Run.ExecutionModeStorageProperty).OriginalValue;
        var current = await ReadStoredAsync(dbContext, run.Id, cancellationToken);
        return current is null || !string.Equals(current, decidedAgainst, StringComparison.Ordinal);
    }

    /// <summary>One atomic <c>ExecuteUpdateAsync</c> whose WHERE requires an Agent-admitting stored form; the
    /// self-referential SET makes a match a no-op write that still takes the database write lock. False means zero rows
    /// matched and the caller must not commit its claim. On a match the tracked Run is aligned to the stored form, so the
    /// claim's later Run UPDATE requires exactly the value this confirmation observed.</summary>
    public static async Task<bool> ConfirmAgentAdmittedAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var runId = run.Id;
        var affectedRowCount = await dbContext.Runs
            .Where(candidate => candidate.Id == runId
                && (EF.Property<string>(candidate, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.ManualAgentText
                    || EF.Property<string>(candidate, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.LegacyText))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => EF.Property<string>(candidate, Run.ExecutionModeStorageProperty),
                    candidate => EF.Property<string>(candidate, Run.ExecutionModeStorageProperty)),
                cancellationToken);
        if (affectedRowCount != 1)
        {
            return false;
        }

        var stored = await ReadStoredAsync(dbContext, runId, cancellationToken);
        if (!RunExecutionModeAdmission.AdmitsAgent(RunExecutionModeStorage.Read(stored)))
        {
            return false;
        }

        var property = dbContext.Entry(run).Property<string>(Run.ExecutionModeStorageProperty);
        property.OriginalValue = stored!;
        property.CurrentValue = stored!;
        return true;
    }
}
