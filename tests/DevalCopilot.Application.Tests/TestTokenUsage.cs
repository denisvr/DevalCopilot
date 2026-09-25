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

    /// <summary>Codex usage as an adapter reports it through the provider-neutral port: never a
    /// cache breakdown.</summary>
    public static AgentTokenUsage CodexReported { get; } = new(2400, 120, null, null, "codex-cli-usage-v1");

    /// <summary>The same Codex usage as Domain evidence.</summary>
    public static AgentTokenUsageEvidence CodexEvidence { get; } =
        AgentTokenUsageEvidence.Create(2400, 120, null, null, "codex-cli-usage-v1");
}
