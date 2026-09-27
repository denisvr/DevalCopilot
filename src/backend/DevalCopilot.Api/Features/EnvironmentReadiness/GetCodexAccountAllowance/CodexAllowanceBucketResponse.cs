namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexAccountAllowance;

/// <summary>One reported allowance bucket — either one entry of the documented
/// <c>rateLimitsByLimitId</c> map (<see cref="LimitId"/> is that entry's own bounded, validated
/// key) or the single legacy <c>rateLimits</c> snapshot (<see cref="LimitId"/> is
/// <see langword="null"/>). Never an aggregate across buckets.</summary>
public sealed record CodexAllowanceBucketResponse(
    string? LimitId, CodexAllowanceWindowResponse? Primary, CodexAllowanceWindowResponse? Secondary);
