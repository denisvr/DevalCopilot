using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// Requests one fresh, read-only, picker-visible Codex model and reasoning-effort catalog
/// observation through the already-vetted local Codex CLI launch target. Never fails in an
/// expected way: an unavailable launch target, an unsupported protocol method, malformed or
/// absent output, timeout, or process failure all resolve to the explicit
/// <see cref="CodexModelCatalogStatus.Unknown"/> projection rather than a query failure. This is
/// catalog evidence only — never selected or effective configuration, authentication readiness,
/// invocation eligibility, or a guarantee that a listed model remains available at dispatch.
/// </summary>
public sealed record GetCodexModelCatalogQuery : IQuery<GetCodexModelCatalogQueryResult>;
