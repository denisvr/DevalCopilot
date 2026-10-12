namespace DevalCopilot.Api.Features.Projects.GetProjectGitHubOrigin;

/// <summary>
/// The closed answer of one repository-local GitHub origin observation. <see cref="Source"/> is always
/// <c>RepositoryLocalConfig</c>; <see cref="Status"/> is one of Unavailable, NotConfigured or Available;
/// <see cref="ObservedAtUtc"/> is present for the last two only and <see cref="Target"/> for Available alone. It is
/// configured scope, never verified remote identity, permission, CI success or publication authority.
/// </summary>
public sealed record ProjectGitHubOriginResponse(
    Guid ProjectId, string Source, string Status, DateTimeOffset? ObservedAtUtc, GitHubOriginTargetResponse? Target);
