namespace DevalCopilot.Api.Features.Runs.GetCodexAccountUsageWarning;

/// <summary>One reported window of a warning check: its bounded bucket identifier (null for the provider's legacy single snapshot),
/// <c>Primary</c> or <c>Secondary</c>, the provider-reported used percentage and whether it reached the saved threshold.</summary>
public sealed record CodexAccountUsageWarningWindowResponse(string? BucketId, string Window, int UsedPercent, bool ReachedThreshold);
