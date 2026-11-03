namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The closed facts about one context's environment map: how many entries, and whether four named kinds are present. Names
/// are tested in memory and only these booleans leave; no name, value or path is reported.</summary>
public sealed record ClaudeStatusEnvironmentFacts(
    int KeyCount,
    bool HasUserProfile,
    bool HasClaudeConfigDirectory,
    bool HasAnthropicVariable,
    bool HasClaudeVariable)
{
    public static ClaudeStatusEnvironmentFacts Describe(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return new ClaudeStatusEnvironmentFacts(0, false, false, false, false);
        }

        bool Has(Func<string, bool> test) => environment.Keys.Any(test);
        return new ClaudeStatusEnvironmentFacts(
            environment.Count,
            Has(key => string.Equals(key, "USERPROFILE", StringComparison.OrdinalIgnoreCase)),
            Has(key => string.Equals(key, "CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase)),
            Has(key => key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase)),
            Has(key => key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)));
    }
}
