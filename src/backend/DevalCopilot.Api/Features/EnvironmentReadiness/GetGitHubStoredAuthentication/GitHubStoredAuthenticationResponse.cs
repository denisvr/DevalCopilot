namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetGitHubStoredAuthentication;

/// <summary>
/// The closed answer for the default stored GitHub CLI authentication of the fixed host. <see cref="Host"/> is always
/// <c>github.com</c> and <see cref="Context"/> always <c>StoredDefaultConfig</c>; <see cref="Status"/> is one of
/// CliUnavailable, Unknown, NoActiveAccount, Authenticated or IssueReported; <see cref="ObservedAtUtc"/> is present only
/// for the last three. It is never repository permission or publication authority and never an environment-token or
/// custom-config observation.
/// </summary>
public sealed record GitHubStoredAuthenticationResponse(string Host, string Context, string Status, DateTimeOffset? ObservedAtUtc);
