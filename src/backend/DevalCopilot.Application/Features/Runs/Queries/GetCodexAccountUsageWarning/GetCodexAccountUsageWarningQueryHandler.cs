using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageWarning;

/// <summary>
/// The explicit advisory warning check (ADR-0026). With no setting, an invalid setting or an unadmitted run it makes no observation.
/// Otherwise it makes exactly one bounded strict observation, outside any transaction, for the exact vetted launch it read, then
/// re-reads the stored setting, the exact stored execution mode (it must still admit Agent work and equal the representation first
/// read) and the launch: a replaced authority can only produce an Unavailable result, never an applicable below-threshold one. It
/// performs no write, keeps no cache and reuses neither the stop's gate, facts or decisions nor the display-only allowance; only
/// the strict immutable observation contract is shared.
/// </summary>
public sealed class GetCodexAccountUsageWarningQueryHandler(
    IDevalCopilotDbContext dbContext, IAccountUsageObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetCodexAccountUsageWarningQuery, Result<GetCodexAccountUsageWarningQueryResult>>
{
    public async Task<Result<GetCodexAccountUsageWarningQueryResult>> HandleAsync(
        GetCodexAccountUsageWarningQuery query, CancellationToken cancellationToken)
    {
        var stored = await ReadStoredWarningAsync(query.RunId, cancellationToken);
        if (!stored.RunExists)
        {
            return Result<GetCodexAccountUsageWarningQueryResult>.Failure(CodexAccountUsageWarningErrors.RunNotFound());
        }

        // The exact stored mode representation, read afresh and untracked; it is retained and compared after the observation.
        var storedMode = await CurrentRunExecutionMode.ReadStoredAsync(dbContext, query.RunId, cancellationToken);
        var executionModeError = RunExecutionModeAdmission.AdmitsAgent(RunExecutionModeStorage.Read(storedMode))
            ? null
            : CurrentRunExecutionMode.NotAdmitted();
        if (executionModeError is not null)
        {
            return Result<GetCodexAccountUsageWarningQueryResult>.Failure(executionModeError);
        }

        var reading = CodexAccountUsageWarning.Read(stored.Text);
        if (reading.IsMalformed)
        {
            return Complete(CodexAccountUsageWarningCheckState.SettingInvalid, null, null);
        }

        if (reading.Value is not { } threshold)
        {
            return Complete(CodexAccountUsageWarningCheckState.NotConfigured, null, null);
        }

        var launch = await ReadLaunchAsync(cancellationToken);
        if (launch is null)
        {
            return Complete(threshold, CodexAccountUsageWarningPolicy.Unavailable(CodexAccountUsageWarningReason.EvidenceUnavailable));
        }

        var readStarted = timeProvider.GetUtcNow();
        AccountUsageObservation observation;
        try
        {
            observation = await observer.ObserveAsync(launch.ExecutablePath, launch.ScriptPath, cancellationToken)
                ?? AccountUsageObservation.Unavailable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            observation = AccountUsageObservation.Unavailable;
        }

        var readCompleted = timeProvider.GetUtcNow();

        var current = await ReadStoredWarningAsync(query.RunId, cancellationToken);
        var currentLaunch = await ReadLaunchAsync(cancellationToken);
        var currentMode = await CurrentRunExecutionMode.ReadStoredAsync(dbContext, query.RunId, cancellationToken);
        var sameAdmittedMode = string.Equals(currentMode, storedMode, StringComparison.Ordinal)
            && RunExecutionModeAdmission.AdmitsAgent(RunExecutionModeStorage.Read(currentMode));
        if (!current.RunExists
            || !sameAdmittedMode
            || !string.Equals(current.Text, stored.Text, StringComparison.Ordinal)
            || currentLaunch != launch)
        {
            return Complete(threshold, CodexAccountUsageWarningPolicy.Unavailable(CodexAccountUsageWarningReason.ConfigurationChanged));
        }

        return Complete(
            threshold,
            CodexAccountUsageWarningPolicy.Evaluate(threshold, observation, readStarted, readCompleted, timeProvider.GetUtcNow()));
    }

    private static Result<GetCodexAccountUsageWarningQueryResult> Complete(
        CodexAccountUsageWarningCheckState state, int? threshold, CodexAccountUsageWarningReason? reason) =>
        Result<GetCodexAccountUsageWarningQueryResult>.Success(new GetCodexAccountUsageWarningQueryResult(
            state, reason, threshold, null, [], false));

    private static Result<GetCodexAccountUsageWarningQueryResult> Complete(
        int threshold, CodexAccountUsageWarningEvaluation evaluation) =>
        Result<GetCodexAccountUsageWarningQueryResult>.Success(new GetCodexAccountUsageWarningQueryResult(
            evaluation.State,
            evaluation.Reason,
            threshold,
            evaluation.ObservedAtUtc,
            evaluation.Windows,
            evaluation.ProviderReportedLimitReached));

    /// <summary>The exact stored text of the warning, read afresh and untracked.</summary>
    private async Task<(bool RunExists, string? Text)> ReadStoredWarningAsync(Guid runId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new { Text = EF.Property<string?>(candidate, Run.CodexAccountUsageWarningStorageProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return (row is not null, row?.Text);
    }

    /// <summary>The currently successful, vetted Codex launch target, or null.</summary>
    private async Task<WarningLaunch?> ReadLaunchAsync(CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        return snapshot is null
            || snapshot.ReasonCode != CapabilityProbeReason.None
            || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath)
                ? null
                : new WarningLaunch(snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath);
    }

    private sealed record WarningLaunch(string ExecutablePath, string? ScriptPath);
}
