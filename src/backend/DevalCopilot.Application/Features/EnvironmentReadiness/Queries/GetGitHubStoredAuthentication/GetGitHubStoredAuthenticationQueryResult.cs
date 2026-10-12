namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// The closed answer: the fixed host and context, a status, and the host's own observation time for the
/// three observed statuses only. Authentication health is never repository permission or publication
/// authority, and environment-token and custom-config contexts are not observed.
/// </summary>
public sealed record GetGitHubStoredAuthenticationQueryResult(GitHubStoredAuthenticationStatus Status, DateTimeOffset? ObservedAtUtc)
{
    public const string FixedHost = "github.com";

    public const string FixedContext = "StoredDefaultConfig";

    public string Host => FixedHost;

    public string Context => FixedContext;

    public static readonly GetGitHubStoredAuthenticationQueryResult CliUnavailable =
        new(GitHubStoredAuthenticationStatus.CliUnavailable, null);

    public static readonly GetGitHubStoredAuthenticationQueryResult Unknown =
        new(GitHubStoredAuthenticationStatus.Unknown, null);
}
