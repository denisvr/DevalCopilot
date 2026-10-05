using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The claim-time account-usage stop shared by the four Codex claim handlers (planning, challenge resolution, code review and
/// verification diagnosis, each with its format-repair path), ADR-0025. With no stop configured it does nothing and makes no
/// observation, so existing claim behavior is unchanged. Otherwise <see cref="CheckClaimAsync"/> observes the account strictly,
/// outside any EF transaction, after the cheap admission and budget checks and before any durable claim, and refuses the claim when the
/// threshold is reached or staying below it cannot be proved. <see cref="ConfirmInTransactionAsync"/> then re-reads the stored setting
/// and launch tuple inside the short claim transaction, before any insert (so the compare and the claim writes are one serialized
/// unit), and re-evaluates freshness at that seam. A refusal commits nothing: no attempt, number, slot, time reservation or consumed
/// authorization. The setting is also an EF concurrency token, so a stale tracked Run can never overwrite a newer setting.
/// A local guard over a provider-reported percentage: below the threshold is never account access, readiness or quota availability.
/// </summary>
public static class CodexAccountUsageStopGate
{
    public const string ReachedCode = "agent_attempts.account_usage_stop_reached";
    public const string EvidenceUnavailableCode = "agent_attempts.account_usage_stop_evidence_unavailable";
    public const string SettingInvalidCode = "agent_attempts.account_usage_stop_setting_invalid";
    public const string PolicyChangedCode = "agent_attempts.account_usage_stop_policy_changed";

    public static Error Reached() => Error.Conflict(
        ReachedCode,
        "This run's configured Codex account-usage stop was reached, so no Codex attempt was started.");

    public static Error EvidenceUnavailable() => Error.Failure(
        EvidenceUnavailableCode,
        "This run's configured Codex account-usage stop cannot be checked right now, so no Codex attempt was started.");

    public static Error SettingInvalid() => Error.Failure(
        SettingInvalidCode,
        "This run's stored Codex account-usage stop is not a valid setting; set or clear it before requesting a Codex attempt.");

    public static Error PolicyChanged() => Error.Conflict(
        PolicyChangedCode,
        "The run's Codex account-usage stop or launch target changed while this attempt was being claimed; retry the request.");

    /// <summary>The refusal for a stop decision: reached states are a conflict, every unavailable state a failure.</summary>
    public static Error ToError(AgentCodexAccountUsageDecision decision) => decision.Kind == CodexAccountUsageDecisionKind.Reached
        ? Reached()
        : decision.Reason == CodexAccountUsageDecisionReason.ThresholdUnusable ? SettingInvalid() : EvidenceUnavailable();

    /// <summary>The currently successful, vetted Codex launch target, read the same way the dispatch path reads it, or null.</summary>
    public static async Task<CodexAccountUsageLaunchTuple?> ReadLaunchTupleAsync(
        IDevalCopilotDbContext dbContext, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        return snapshot is null || snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath)
            ? null
            : new CodexAccountUsageLaunchTuple(snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath);
    }

    /// <summary>The early check. <paramref name="adapter"/> may be null only where no observation capability is wired; a configured
    /// stop then refuses (a missing capability can never bypass the stop).</summary>
    public static async Task<Result<CodexAccountUsageClaimGuard>> CheckClaimAsync(
        IDevalCopilotDbContext dbContext,
        IAccountUsageObserver? adapter,
        TimeProvider timeProvider,
        Run run,
        CancellationToken cancellationToken)
    {
        var stored = dbContext.Entry(run).Property<string?>(Run.CodexAccountUsageStopStorageProperty).CurrentValue;
        var reading = CodexAccountUsageStop.Read(stored);
        if (reading.IsMalformed)
        {
            return Result<CodexAccountUsageClaimGuard>.Failure(SettingInvalid());
        }

        if (reading.Value is not { } threshold)
        {
            return Result<CodexAccountUsageClaimGuard>.Success(new CodexAccountUsageClaimGuard(null, stored, null));
        }

        var tuple = await ReadLaunchTupleAsync(dbContext, cancellationToken);
        if (tuple is null || adapter is null)
        {
            return Result<CodexAccountUsageClaimGuard>.Failure(EvidenceUnavailable());
        }

        var facts = await ObserveAsync(adapter, timeProvider, attemptId: null, threshold, tuple, cancellationToken);
        var evaluation = CodexAccountUsageStopPolicy.Evaluate(facts, threshold, timeProvider.GetUtcNow());
        return evaluation.StopDecision is { } decision
            ? Result<CodexAccountUsageClaimGuard>.Failure(ToError(decision))
            : Result<CodexAccountUsageClaimGuard>.Success(new CodexAccountUsageClaimGuard(threshold, stored, facts));
    }

    /// <summary>One fresh observation of the account for exactly this attempt (or claim), threshold and launch tuple, with the host's
    /// read interval around it. A failing adapter is an unavailable observation, never an exception the caller must handle; only the
    /// caller's own cancellation propagates.</summary>
    public static async Task<CodexAccountUsageGuardFacts> ObserveAsync(
        IAccountUsageObserver adapter,
        TimeProvider timeProvider,
        Guid? attemptId,
        int thresholdPercent,
        CodexAccountUsageLaunchTuple tuple,
        CancellationToken cancellationToken)
    {
        var readStarted = timeProvider.GetUtcNow();
        AccountUsageObservation observation;
        try
        {
            observation = await adapter.ObserveAsync(tuple.ExecutablePath, tuple.ScriptPath, cancellationToken)
                ?? AccountUsageObservation.Unavailable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            observation = AccountUsageObservation.Unavailable;
        }

        var readCompleted = timeProvider.GetUtcNow();
        return new CodexAccountUsageGuardFacts(
            attemptId, thresholdPercent, tuple.ExecutablePath, tuple.ScriptPath, observation, readStarted, readCompleted);
    }

    /// <summary>The seam inside the short claim transaction, called before any insert. Returns null when the claim may proceed.</summary>
    public static async Task<Error?> ConfirmInTransactionAsync(
        IDevalCopilotDbContext dbContext,
        TimeProvider timeProvider,
        Run run,
        CodexAccountUsageClaimGuard guard,
        CancellationToken cancellationToken)
    {
        if (!await ConfirmStoredUnchangedAsync(dbContext, run.Id, guard.StoredText, cancellationToken))
        {
            return PolicyChanged();
        }

        if (guard.ThresholdPercent is not { } threshold)
        {
            return null;
        }

        var facts = guard.Facts;
        var tuple = await ReadLaunchTupleAsync(dbContext, cancellationToken);
        if (facts is null || tuple is null || tuple.ExecutablePath != facts.ExecutablePath || tuple.ScriptPath != facts.ScriptPath)
        {
            return PolicyChanged();
        }

        var evaluation = CodexAccountUsageStopPolicy.Evaluate(facts, threshold, timeProvider.GetUtcNow());
        return evaluation.StopDecision is { } decision ? ToError(decision) : null;
    }

    /// <summary>Records the claim's setting on the new attempt (a no-op when the stop is disabled).</summary>
    public static void Snapshot(Attempt attempt, CodexAccountUsageClaimGuard guard)
    {
        if (guard.ThresholdPercent is { } threshold)
        {
            attempt.SnapshotCodexAccountUsageStop(threshold);
        }
    }

    /// <summary>After a concurrency failure of the claim's commit: whether the stored setting differs from the one the claim used.</summary>
    public static async Task<bool> HasChangedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CodexAccountUsageClaimGuard guard, CancellationToken cancellationToken)
    {
        var current = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new { Stored = EF.Property<string?>(candidate, Run.CodexAccountUsageStopStorageProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return current is null || !string.Equals(current.Stored, guard.StoredText, StringComparison.Ordinal);
    }

    private static async Task<bool> ConfirmStoredUnchangedAsync(
        IDevalCopilotDbContext dbContext, Guid runId, string? storedText, CancellationToken cancellationToken)
    {
        var affectedRowCount = await dbContext.Runs
            .Where(candidate => candidate.Id == runId
                && EF.Property<string?>(candidate, Run.CodexAccountUsageStopStorageProperty) == storedText)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);

        return affectedRowCount == 1;
    }
}
