namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One model identifier a provider listed in its own result, with the context-window and maximum-output token
/// limits it reported for that identifier. The identifier is exactly the provider's text (never normalized, aliased,
/// or substituted) and the entry is neither the main model nor a fallback: it only says what the provider listed.
/// </summary>
public sealed record AgentModelContextLimit(string ModelId, int ContextWindowTokens, int MaxOutputTokens);
