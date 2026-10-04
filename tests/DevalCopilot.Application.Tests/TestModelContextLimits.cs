using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Tests;

/// <summary>Deterministic provider-reported model context limits for tests. Never produced by a real provider process.</summary>
internal static class TestModelContextLimits
{
    public const string Source = "claude-cli-model-usage-v1";

    /// <summary>The exact canonical text the project persists for <see cref="Reported"/>, written independently of the
    /// production serializer.</summary>
    public const string Snapshot =
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-a\",\"contextWindowTokens\":200000,"
        + "\"maxOutputTokens\":32000},{\"modelId\":\"claude-b\",\"contextWindowTokens\":1000000,\"maxOutputTokens\":64000}]}";

    /// <summary>Two models listed in the reverse of their ordinal order, as an adapter reports them through the
    /// provider-neutral port.</summary>
    public static AgentModelContextLimits Reported { get; } = new(
        Source,
        [
            new AgentModelContextLimitEntry("claude-b", 1000000, 64000),
            new AgentModelContextLimitEntry("claude-a", 200000, 32000),
        ]);

    /// <summary>A report no proven contract produces: an unknown source tag.</summary>
    public static AgentModelContextLimits Unproven { get; } =
        new("unproven-model-usage-v1", [new AgentModelContextLimitEntry("claude-a", 200000, 32000)]);
}
