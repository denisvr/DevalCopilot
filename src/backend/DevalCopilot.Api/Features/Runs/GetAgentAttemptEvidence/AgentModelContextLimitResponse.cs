namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>
/// One model identifier the provider listed in its own result for the attempt, with the context-window and
/// maximum-output token limits it reported for that identifier. A fact of what the provider reported, never the main
/// model, a fallback, proof that the model was used, remaining context, or next-invocation capacity.
/// </summary>
public sealed record AgentModelContextLimitResponse(string ModelId, int ContextWindowTokens, int MaxOutputTokens);
