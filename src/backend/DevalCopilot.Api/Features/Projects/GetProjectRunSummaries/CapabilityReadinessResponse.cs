namespace DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;

/// <param name="DisplayStatus">One of "Ready", "Degraded", "NeedsAttention", "Unavailable", or
/// null while the capability has never been probed yet (a loading presentation, never a fifth
/// status).</param>
/// <param name="IsStale">An independent qualifier on top of <paramref name="DisplayStatus"/>.</param>
public sealed record CapabilityReadinessResponse(
    string Capability,
    bool IsRequired,
    string? DisplayStatus,
    string ReasonCode,
    string? Version,
    DateTimeOffset? LastCheckedUtc,
    bool IsStale);
