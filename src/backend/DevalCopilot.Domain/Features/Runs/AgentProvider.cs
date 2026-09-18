namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of real agent providers an <see cref="Attempt"/> of <see cref="AttemptKind.Agent"/>
/// can target. Appended, never renumbered, as a slice adds a real, proven provider.
/// </summary>
public enum AgentProvider
{
    Codex = 0,

    /// <summary>Claude Code, added for durable checkpoint-bound critical review.</summary>
    ClaudeCode = 1,
}
