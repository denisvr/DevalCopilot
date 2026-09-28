namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexModelCatalog;

/// <summary>One picker-visible Codex model. <see cref="SupportedReasoningEfforts"/> lists the
/// effort identifiers this model accepts, or is <see langword="null"/> (Unknown) when the
/// reported list could not be trusted as a whole; <see cref="DefaultReasoningEffort"/> is the
/// provider's own suggested default, or <see langword="null"/> when absent, untrusted, or not
/// itself among a known supported-effort list.</summary>
public sealed record CodexModelCatalogEntryResponse(
    string Id, string DisplayName, IReadOnlyList<string>? SupportedReasoningEfforts, string? DefaultReasoningEffort);
