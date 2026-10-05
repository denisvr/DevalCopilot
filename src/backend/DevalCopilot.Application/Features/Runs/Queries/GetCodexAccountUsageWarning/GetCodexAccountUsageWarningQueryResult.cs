using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageWarning;

/// <summary>
/// The advisory outcome of one explicit warning check. <see cref="ThresholdPercent"/> is the saved threshold the check was made for
/// (null when nothing was configured or the stored value is invalid). <see cref="ObservedAtUtc"/> and <see cref="Windows"/> are
/// present only for Below and Reached; an Unavailable result carries neither. A fact about one read of the host's Codex account:
/// never usage attributable to the run, eligibility, readiness or remaining capacity.
/// </summary>
public sealed record GetCodexAccountUsageWarningQueryResult(
    CodexAccountUsageWarningCheckState State,
    CodexAccountUsageWarningReason? Reason,
    int? ThresholdPercent,
    DateTimeOffset? ObservedAtUtc,
    IReadOnlyList<CodexAccountUsageWarningWindow> Windows,
    bool ProviderReportedLimitReached);
