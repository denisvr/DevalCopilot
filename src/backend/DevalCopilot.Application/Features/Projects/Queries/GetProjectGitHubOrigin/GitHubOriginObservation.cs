namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// What <see cref="IGitHubOriginObserver"/> reports: provider-neutral and already reduced to the closed status. Only
/// <see cref="GitHubOriginStatus.Available"/> carries a target; no raw URL, configuration, stderr or rejected value
/// ever crosses this port. The observer assigns no time; the query stamps the observation it accepts.
/// </summary>
public sealed record GitHubOriginObservation(GitHubOriginStatus Status, GitHubOriginTarget? Target)
{
    public static readonly GitHubOriginObservation Unavailable = new(GitHubOriginStatus.Unavailable, null);

    public static readonly GitHubOriginObservation NotConfigured = new(GitHubOriginStatus.NotConfigured, null);
}
