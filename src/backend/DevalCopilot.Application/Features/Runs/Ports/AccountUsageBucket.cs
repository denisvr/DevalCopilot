namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>One metered bucket of a strict observation: its bounded identifier (null for the provider's legacy single snapshot) and
/// its primary and secondary windows, each absent only when the provider documented it as absent. At least one window is present.</summary>
public sealed record AccountUsageBucket(string? Id, AccountUsageWindow? Primary, AccountUsageWindow? Secondary);
