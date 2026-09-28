namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// A read-only, picker-visible Codex model and reasoning-effort catalog observation. This is
/// catalog evidence, never selected or effective configuration, authentication readiness, or a
/// guarantee that a listed model remains available at dispatch. <see cref="RetrievedAtUtc"/> is
/// the host's own clock reading at the moment this snapshot was obtained — never a provider-
/// reported timestamp — and is populated only alongside <see cref="CodexModelCatalogStatus.Observed"/>,
/// alongside at least one <see cref="Models"/> entry. A genuinely unavailable catalog uses
/// <see cref="Unknown"/> instead of reporting <see cref="CodexModelCatalogStatus.Observed"/> with
/// an empty model list.
/// </summary>
public sealed record GetCodexModelCatalogQueryResult(
    CodexModelCatalogStatus Status,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexModelCatalogEntry> Models)
{
    public static readonly GetCodexModelCatalogQueryResult Unknown = new(CodexModelCatalogStatus.Unknown, null, []);
}
