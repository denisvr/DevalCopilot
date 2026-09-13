namespace DevalCopilot.Domain.Features.EnvironmentReadiness;

/// <summary>
/// The fixed, closed set of local tool capabilities Increment 2 discovers. Never
/// user-extensible: adding a capability is a code change to this catalog, not project
/// configuration.
/// </summary>
public enum Capability
{
    Git = 0,
    CodexCli = 1,
    ClaudeCli = 2,
    GitHubCli = 3,
    DotNetSdk = 4,
    Node = 5,
    Docker = 6,
}
