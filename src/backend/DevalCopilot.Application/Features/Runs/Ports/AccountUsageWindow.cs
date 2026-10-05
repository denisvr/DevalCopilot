namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>One usage window the strict observation validated: the provider-reported used percentage from 0 through 100 and, when the
/// provider gave a valid one, the instant it says the window resets. Neither field is ever clamped, defaulted or inferred.</summary>
public sealed record AccountUsageWindow(int UsedPercent, DateTimeOffset? ResetsAtUtc);
