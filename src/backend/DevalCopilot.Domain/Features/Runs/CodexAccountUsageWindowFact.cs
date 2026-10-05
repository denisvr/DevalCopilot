namespace DevalCopilot.Domain.Features.Runs;

/// <summary>One validated usage window a stop decision used: its bounded bucket identifier (null for the provider's legacy single
/// snapshot), which window of the bucket it was, and its reported used percentage from 0 through 100.</summary>
public sealed record CodexAccountUsageWindowFact(string? BucketId, CodexAccountUsageWindowKind Window, int UsedPercent);
