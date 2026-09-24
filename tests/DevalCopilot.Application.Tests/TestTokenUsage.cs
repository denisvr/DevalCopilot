using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests;

/// <summary>Deterministic provider-reported token usage for tests. Never produced by a real
/// provider process.</summary>
internal static class TestTokenUsage
{
    /// <summary>Usage as an adapter reports it through the provider-neutral port.</summary>
    public static AgentTokenUsage Reported { get; } = new(1200, 345, 67, 890, "claude-cli-usage-v1");

    /// <summary>The same usage as Domain evidence.</summary>
    public static AgentTokenUsageEvidence Evidence { get; } =
        AgentTokenUsageEvidence.Create(1200, 345, 67, 890, "claude-cli-usage-v1");
}
