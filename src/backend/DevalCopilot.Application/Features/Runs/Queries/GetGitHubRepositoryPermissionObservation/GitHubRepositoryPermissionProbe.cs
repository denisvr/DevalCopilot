namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>What <see cref="IGitHubRepositoryPermissionObserver"/> reports for one admitted answer: the opaque repository node identifier,
/// the names GitHub returned and GitHub's literal viewer permission (<see langword="null"/> when GitHub returned an explicit null).</summary>
public sealed record GitHubRepositoryPermissionProbe(string NodeId, string Owner, string Repository, string? ViewerPermission);
