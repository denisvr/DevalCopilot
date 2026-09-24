using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Domain.Tests;

/// <summary>Deterministic provider-reported token usage for tests. Never produced by a real
/// provider process.</summary>
internal static class TestTokenUsage
{
    public static AgentTokenUsageEvidence Reported { get; } =
        AgentTokenUsageEvidence.Create(1200, 345, 67, 890, "claude-cli-usage-v1");

    /// <summary>Claude usage with absent optional cache breakdown.</summary>
    public static AgentTokenUsageEvidence InputOutputOnly { get; } =
        AgentTokenUsageEvidence.Create(500, 60, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);
}
