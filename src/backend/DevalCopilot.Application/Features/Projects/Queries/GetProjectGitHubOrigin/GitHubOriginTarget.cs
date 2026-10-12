namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// The normalized, supported subset of a repository-local GitHub origin. The host is the fixed literal
/// <c>github.com</c>; the owner and repository are the ASCII-validated path segments with one optional terminal
/// <c>.git</c> removed. It is configured scope, never verified remote identity, permission or publication authority.
/// </summary>
public sealed record GitHubOriginTarget(string Owner, string Repository)
{
    public const string FixedHost = "github.com";

    public string Host => FixedHost;
}
