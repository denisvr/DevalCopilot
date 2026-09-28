namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexModelCatalog;

/// <summary>
/// A read-only, picker-visible Codex model and reasoning-effort catalog observation. <c>Unknown</c>
/// covers every unavailable case uniformly; only <c>Observed</c> carries <see cref="RetrievedAtUtc"/>
/// and any model entries. This is catalog evidence only — never selected or effective
/// configuration, authentication readiness, invocation eligibility, or a guarantee that a listed
/// model remains available at dispatch.
/// </summary>
public sealed record CodexModelCatalogResponse(
    string Status,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexModelCatalogEntryResponse> Models);
