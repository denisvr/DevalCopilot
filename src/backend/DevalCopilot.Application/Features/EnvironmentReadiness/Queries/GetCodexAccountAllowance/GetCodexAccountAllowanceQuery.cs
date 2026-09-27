using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// Requests one fresh, read-only Codex ChatGPT account-allowance snapshot through the
/// already-vetted local Codex CLI launch target. Never fails in an expected way: an
/// unavailable launch target, missing authentication, an unsupported protocol, malformed
/// output, timeout, or process failure all resolve to the explicit
/// <see cref="CodexAccountAllowanceStatus.Unknown"/> projection rather than a query failure.
/// </summary>
public sealed record GetCodexAccountAllowanceQuery : IQuery<GetCodexAccountAllowanceQueryResult>;
