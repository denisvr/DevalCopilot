namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexAccountAllowance;

/// <summary>
/// A read-only Codex account-allowance snapshot. <c>Unknown</c> covers every unavailable case
/// uniformly; only <c>Observed</c> carries <see cref="RetrievedAtUtc"/> and any window data.
/// This is provider-reported evidence, never an enforceable stop threshold or a guarantee that a
/// specific invocation is currently eligible to start.
/// </summary>
public sealed record CodexAccountAllowanceResponse(
    string Status,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexAllowanceBucketResponse> Buckets);
