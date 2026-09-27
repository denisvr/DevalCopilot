namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// Observes the Codex ChatGPT account-allowance snapshot for one already-vetted local Codex CLI
/// launch target. A single, bounded, read-only interaction — this port grants no resume,
/// dispatch, or mutating capability of any kind, and it never fails in a way the caller needs
/// to catch: every unavailable case (missing authentication, an unsupported protocol method, a
/// malformed response, a timeout, a process failure, or cancellation propagated from the
/// caller) resolves through <see cref="CodexAccountAllowanceObservation.Unknown"/> or an
/// <see cref="OperationCanceledException"/> raised only for the caller's own cancellation.
/// </summary>
public interface ICodexAccountAllowanceAdapter
{
    Task<CodexAccountAllowanceObservation> ObserveAsync(
        string executablePath, string? scriptPath, CancellationToken cancellationToken);
}
