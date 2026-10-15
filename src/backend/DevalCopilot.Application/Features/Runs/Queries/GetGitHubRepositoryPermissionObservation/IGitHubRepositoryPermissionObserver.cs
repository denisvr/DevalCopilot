namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// Observes, once and read-only, the repository identity and the viewer permission GitHub reports for one GitHub repository named by a
/// recorded scope, through one already-vetted direct executable and the default stored authentication. The permission is a reported fact,
/// never a derived capability, and nothing here reads credentials. Every unusable outcome (failure, timeout, malformed, foreign, inaccessible
/// or oversized answer, an unknown permission literal, a concurrent observation) is <see langword="null"/>; only the caller's own
/// cancellation and unexpected faults are raised.
/// </summary>
public interface IGitHubRepositoryPermissionObserver
{
    Task<GitHubRepositoryPermissionProbe?> ObserveAsync(
        string executablePath, string owner, string repository, CancellationToken cancellationToken);
}
