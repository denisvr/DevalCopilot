namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// A read-only Codex account-allowance observation. This is a provider-reported snapshot, never
/// an enforceable stop threshold and never a guarantee that any particular invocation is
/// currently eligible to start. <see cref="RetrievedAtUtc"/> is the host's own clock reading at
/// the moment this snapshot was obtained — never a provider-reported timestamp — and is
/// populated only alongside <see cref="CodexAccountAllowanceStatus.Observed"/>, alongside at
/// least one <see cref="Buckets"/> entry. A genuinely unavailable snapshot uses
/// <see cref="Unknown"/> instead of reporting <see cref="CodexAccountAllowanceStatus.Observed"/>
/// with an empty bucket list.
/// </summary>
public sealed record GetCodexAccountAllowanceQueryResult(
    CodexAccountAllowanceStatus Status,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexAllowanceBucket> Buckets)
{
    public static readonly GetCodexAccountAllowanceQueryResult Unknown = new(CodexAccountAllowanceStatus.Unknown, null, []);
}
