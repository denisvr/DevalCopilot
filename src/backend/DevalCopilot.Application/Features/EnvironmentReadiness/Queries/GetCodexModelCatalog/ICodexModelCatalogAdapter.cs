namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// Observes the Codex model and reasoning-effort catalog for one already-vetted local Codex CLI
/// launch target. A single, bounded, read-only interaction — this port grants no selection,
/// dispatch, or mutating capability of any kind, and it never fails in a way the caller needs to
/// catch: every unavailable case (missing authentication, an unsupported protocol method, a
/// malformed or excessive response, a timeout, a process failure, or cancellation propagated from
/// the caller) resolves through <see cref="CodexModelCatalogObservation.Unknown"/> or an
/// <see cref="OperationCanceledException"/> raised only for the caller's own cancellation.
/// </summary>
public interface ICodexModelCatalogAdapter
{
    Task<CodexModelCatalogObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken);
}
