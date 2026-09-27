namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// One reported allowance window. Bounded to exactly the fields the documented response
/// carries for a window — never a raw provider payload.
/// <paramref name="UsedPercent"/> is the one field a window must carry to exist at all: it is
/// validated to the inclusive range [0, 100], and a window whose <c>usedPercent</c> is missing
/// or fails that check is never constructed — its absence is projected as <see langword="null"/>
/// instead, exactly like a window the provider never reported. <paramref name="WindowDurationMins"/>
/// and <paramref name="ResetsAtUtc"/> are the response's own optional/nullable fields: each is
/// independently <see langword="null"/> when the provider omitted it or reported it in a shape
/// this projection does not trust, without invalidating the rest of the window.
/// </summary>
public sealed record CodexAllowanceWindow(int UsedPercent, int? WindowDurationMins, DateTimeOffset? ResetsAtUtc);
