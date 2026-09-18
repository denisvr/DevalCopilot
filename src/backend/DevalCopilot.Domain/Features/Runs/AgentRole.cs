namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of real collaboration roles an <see cref="Attempt"/> of <see cref="AttemptKind.Agent"/>
/// can occupy. Deliberately just one member for this slice — planning only, never critical review,
/// resolution, or execution. Appended, never renumbered, when a future slice adds a real role.
/// </summary>
public enum AgentRole
{
    Planner = 0,
}
