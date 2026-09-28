namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// The explicit observation-state discriminator for a Codex model catalog. <see cref="Unknown"/>
/// covers every unavailable case uniformly (no vetted launch target, missing account
/// authentication, an unsupported protocol method, a malformed, duplicate, conflicting, or
/// excessive response, a timeout, or a process failure) — never a guessed or empty-looking
/// catalog presented as complete.
/// </summary>
public enum CodexModelCatalogStatus
{
    Unknown = 0,
    Observed = 1,
}
