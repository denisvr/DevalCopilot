namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of real collaboration roles an <see cref="Attempt"/> of <see cref="AttemptKind.Agent"/>
/// can occupy. Appended, never renumbered, as a slice adds a real, proven role.
/// </summary>
public enum AgentRole
{
    Planner = 0,

    /// <summary>Claude Code reviewing a real, current Codex Proposal against the same run,
    /// isolated workspace, checkpoint, and fresh Git fingerprint. Never resolution, revision, or
    /// execution — those remain deferred.</summary>
    CriticalReviewer = 1,
}
