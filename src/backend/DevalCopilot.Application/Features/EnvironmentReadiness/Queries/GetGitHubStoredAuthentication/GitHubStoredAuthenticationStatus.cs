namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// The closed observation state of the default stored GitHub CLI authentication for github.com.
/// Only <see cref="NoActiveAccount"/>, <see cref="Authenticated"/> and <see cref="IssueReported"/> are
/// observations and carry a time; <see cref="CliUnavailable"/> means nothing was invoked and
/// <see cref="Unknown"/> covers every other unusable outcome.
/// </summary>
public enum GitHubStoredAuthenticationStatus
{
    CliUnavailable = 0,
    Unknown = 1,
    NoActiveAccount = 2,
    Authenticated = 3,
    IssueReported = 4,
}
