namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The evaluation of one observation against one saved warning threshold. Windows and the observation instant are present
/// only for Below and Reached; an Unavailable evaluation carries neither, so no valid subset can leak.</summary>
public sealed record CodexAccountUsageWarningEvaluation(
    CodexAccountUsageWarningCheckState State,
    CodexAccountUsageWarningReason? Reason,
    DateTimeOffset? ObservedAtUtc,
    IReadOnlyList<CodexAccountUsageWarningWindow> Windows,
    bool ProviderReportedLimitReached);
