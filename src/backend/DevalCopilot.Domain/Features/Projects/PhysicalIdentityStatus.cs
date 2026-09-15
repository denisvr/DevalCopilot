namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// Whether a project's Windows physical repository identity (volume serial number + 128-bit
/// file ID) has been established. <see cref="Unresolved"/> is the default for every project —
/// including one that predates this capability — until an explicit resolution attempt runs.
/// See ADR-0008.
/// </summary>
public enum PhysicalIdentityStatus
{
    Unresolved = 0,
    Resolved = 1,
    Unavailable = 2,
}
