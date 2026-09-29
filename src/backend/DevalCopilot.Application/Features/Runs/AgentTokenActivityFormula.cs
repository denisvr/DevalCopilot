using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The single provider-specific rule turning one concluded attempt's validated
/// <see cref="AgentTokenUsageEvidence"/> into its reported token-activity count, shared by the
/// advisory warning and the claim stop so their counts can never drift. Codex counts input + output
/// (its cached input is already inside input and is not added again); Claude Code counts input +
/// cache creation + cache read + output and treats a row missing either cache count as insufficient
/// rather than substituting zero. The two providers' counts are never combined.
/// </summary>
internal static class AgentTokenActivityFormula
{
    /// <summary>The provider's count for this evidence, or <see langword="null"/> when the evidence
    /// is absent or insufficient for the provider's formula.</summary>
    public static long? Count(AgentProvider provider, AgentTokenUsageEvidence? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return provider switch
        {
            AgentProvider.Codex => (long)usage.InputTokens + usage.OutputTokens,
            AgentProvider.ClaudeCode when usage.CacheCreationInputTokens is { } creation && usage.CacheReadInputTokens is { } read =>
                (long)usage.InputTokens + creation + read + usage.OutputTokens,
            _ => null,
        };
    }
}
