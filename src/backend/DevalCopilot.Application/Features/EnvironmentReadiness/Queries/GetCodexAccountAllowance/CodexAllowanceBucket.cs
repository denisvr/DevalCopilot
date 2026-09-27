namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// One reported allowance bucket — either one entry of the documented <c>rateLimitsByLimitId</c>
/// map (<paramref name="LimitId"/> is that entry's own bounded, validated key) or the single
/// legacy <c>rateLimits</c> snapshot (<paramref name="LimitId"/> is <see langword="null"/> — the
/// legacy response carries no id of its own). Each bucket is represented independently; no
/// aggregate is ever computed or displayed across buckets, and the legacy and multi-bucket views
/// are never combined into one result.
/// </summary>
public sealed record CodexAllowanceBucket(string? LimitId, CodexAllowanceWindow? Primary, CodexAllowanceWindow? Secondary);
