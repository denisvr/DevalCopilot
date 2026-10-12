namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// The closed answer: the project, a status, the host's own observation time for the two observed statuses and the
/// normalized target for <see cref="GitHubOriginStatus.Available"/> alone. The source is always the repository's own
/// local configuration; the answer is configured scope, never verified remote identity, permission, CI success or
/// publication authority.
/// </summary>
public sealed record GetProjectGitHubOriginQueryResult(
    Guid ProjectId, GitHubOriginStatus Status, DateTimeOffset? ObservedAtUtc, GitHubOriginTarget? Target)
{
    public const string FixedSource = "RepositoryLocalConfig";

    public string Source => FixedSource;

    public static GetProjectGitHubOriginQueryResult Unavailable(Guid projectId) =>
        new(projectId, GitHubOriginStatus.Unavailable, null, null);
}
