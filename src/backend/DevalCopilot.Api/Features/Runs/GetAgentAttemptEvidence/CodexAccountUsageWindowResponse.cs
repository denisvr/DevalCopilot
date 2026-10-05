namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>One validated usage window a stop decision used: the bounded bucket identifier (null for the provider's legacy single
/// snapshot), <c>Primary</c> or <c>Secondary</c>, and the used percentage from 0 through 100.</summary>
public sealed record CodexAccountUsageWindowResponse(string? BucketId, string Window, int UsedPercent);
