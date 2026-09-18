namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of real agent providers an <see cref="Attempt"/> of <see cref="AttemptKind.Agent"/>
/// can target. Deliberately just one member for this slice — Codex planning only — rather than a
/// speculative placeholder for Claude Code, which has no real invocation path yet. Appended, never
/// renumbered, when a future slice adds a real provider.
/// </summary>
public enum AgentProvider
{
    Codex = 0,
}
