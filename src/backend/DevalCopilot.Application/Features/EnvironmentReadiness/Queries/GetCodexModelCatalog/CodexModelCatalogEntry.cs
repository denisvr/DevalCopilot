namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// One picker-visible Codex model reported by the documented <c>model/list</c> method.
/// <see cref="SupportedReasoningEfforts"/> lists the bounded, validated effort identifiers this
/// model accepts; it is <see langword="null"/> (Unknown) when any individual reported effort was
/// malformed or duplicated — never a partial list with the bad element silently dropped — and an
/// empty (non-null) list only when the provider genuinely reported none.
/// <see cref="DefaultReasoningEffort"/> is the provider's own suggested default, projected as
/// <see langword="null"/> when absent, reported in a shape this projection does not trust, or not
/// itself a member of a known <see cref="SupportedReasoningEfforts"/> list. Catalog evidence
/// only — never a claim that this model remains available, authenticated, or eligible to invoke
/// at dispatch time.
/// </summary>
public sealed record CodexModelCatalogEntry(
    string Id,
    string DisplayName,
    IReadOnlyList<string>? SupportedReasoningEfforts,
    string? DefaultReasoningEffort);
