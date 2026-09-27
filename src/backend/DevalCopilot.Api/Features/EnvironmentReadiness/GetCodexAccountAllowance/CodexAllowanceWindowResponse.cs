namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexAccountAllowance;

/// <summary>One reported allowance window. Bounded to exactly these three fields — never a raw
/// provider payload. <see cref="WindowDurationMins"/> and <see cref="ResetsAtUtc"/> are
/// independently optional per the documented response.</summary>
public sealed record CodexAllowanceWindowResponse(int UsedPercent, int? WindowDurationMins, DateTimeOffset? ResetsAtUtc);
