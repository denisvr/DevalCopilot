namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>One bounded, safe fact of a warning check: the observation's bucket identifier (null for the provider's legacy single
/// snapshot), the window kind, the provider-reported used percentage and whether it reached the saved threshold.</summary>
public sealed record CodexAccountUsageWarningWindow(
    string? BucketId, CodexAccountUsageWarningWindowKind Kind, int UsedPercent, bool ReachedThreshold);
