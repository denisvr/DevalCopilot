namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// One model identifier a provider listed in its own result and the context-window and maximum-output token limits it
/// reported for that identifier, exactly as the adapter parsed them (never normalized, aliased, or substituted).
/// </summary>
public sealed record AgentModelContextLimitEntry(string ModelId, int ContextWindowTokens, int MaxOutputTokens);
