namespace DevalCopilot.Api.Features.Runs.GetCodexAccountUsageWarning;

/// <summary>
/// The advisory outcome of one explicit warning check (ADR-0026). <c>State</c> is <c>NotConfigured</c>, <c>SettingInvalid</c>,
/// <c>Unavailable</c>, <c>Below</c> or <c>Reached</c>; <c>Reason</c> is present for <c>Reached</c> (<c>ThresholdReached</c> or
/// <c>ProviderReportedLimitReached</c>) and <c>Unavailable</c> (<c>EvidenceUnavailable</c>, <c>EvidenceExpired</c> or
/// <c>ConfigurationChanged</c>). <c>ThresholdPercent</c> is the saved threshold the check concerned. <c>ObservedAtUtc</c> and
/// <c>Windows</c> are present only for <c>Below</c> and <c>Reached</c>. A fact about one read of the host's Codex account: never
/// usage attributable to the run, eligibility, readiness or remaining capacity.
/// </summary>
public sealed record GetCodexAccountUsageWarningResponse(
    string State,
    string? Reason,
    int? ThresholdPercent,
    DateTimeOffset? ObservedAtUtc,
    IReadOnlyList<CodexAccountUsageWarningWindowResponse> Windows,
    bool ProviderReportedLimitReached);
