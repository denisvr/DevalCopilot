namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// What <see cref="IGitHubStoredAuthenticationObserver"/> reports. Provider-neutral and already reduced
/// to the closed status: no account, token, scope or error text ever crosses this port.
/// </summary>
public sealed record GitHubStoredAuthenticationObservation(GitHubStoredAuthenticationStatus Status, DateTimeOffset? ObservedAtUtc)
{
    public static readonly GitHubStoredAuthenticationObservation Unknown = new(GitHubStoredAuthenticationStatus.Unknown, null);
}
