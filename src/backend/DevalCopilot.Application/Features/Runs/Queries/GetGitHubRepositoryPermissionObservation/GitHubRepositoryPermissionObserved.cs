namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>The validated facts of an admitted answer, stamped with the host clock after the recorded scope and binding were confirmed
/// unchanged. It is a point-in-time report, never a binding, an approval or a push permission. <c>ViewerPermission</c> is
/// <see langword="null"/> exactly for <see cref="GitHubRepositoryPermissionObservationStatus.PermissionNotReturned"/>.</summary>
public sealed record GitHubRepositoryPermissionObserved(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, string? ViewerPermission);
