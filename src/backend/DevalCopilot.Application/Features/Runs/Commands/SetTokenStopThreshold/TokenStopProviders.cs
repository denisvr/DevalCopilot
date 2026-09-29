using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

/// <summary>The closed, case-sensitive wire names for the two providers a token stop threshold can
/// target. Exactly the names <see cref="AgentProvider"/> serializes as elsewhere in the API.</summary>
internal static class TokenStopProviders
{
    public static bool TryParse(string? value, out AgentProvider provider)
    {
        switch (value)
        {
            case nameof(AgentProvider.Codex):
                provider = AgentProvider.Codex;
                return true;
            case nameof(AgentProvider.ClaudeCode):
                provider = AgentProvider.ClaudeCode;
                return true;
            default:
                provider = default;
                return false;
        }
    }
}
