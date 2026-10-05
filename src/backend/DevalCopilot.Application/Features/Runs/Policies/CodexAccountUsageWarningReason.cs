namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>Why a check is Reached or Unavailable. Below, NotConfigured and SettingInvalid carry no reason.</summary>
public enum CodexAccountUsageWarningReason
{
    ThresholdReached = 1,
    ProviderReportedLimitReached = 2,
    EvidenceUnavailable = 3,
    EvidenceExpired = 4,
    ConfigurationChanged = 5,
}
