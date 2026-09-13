namespace DevalCopilot.Domain.Features.EnvironmentReadiness;

/// <summary>
/// The fixed catalog of capabilities every project observes, and the default requirement
/// classification applied when projecting shared host evidence for a project. Every project
/// uses this same default in Increment 2; a project-specific override is a future seam, not
/// implemented here.
/// </summary>
public static class CapabilityCatalog
{
    public static readonly IReadOnlyList<Capability> All =
    [
        Capability.Git,
        Capability.CodexCli,
        Capability.ClaudeCli,
        Capability.GitHubCli,
        Capability.DotNetSdk,
        Capability.Node,
        Capability.Docker,
    ];

    /// <summary>Docker is the only optional capability; every other catalog entry is required
    /// by default for every project.</summary>
    public static bool IsRequiredByDefault(Capability capability) => capability != Capability.Docker;
}
