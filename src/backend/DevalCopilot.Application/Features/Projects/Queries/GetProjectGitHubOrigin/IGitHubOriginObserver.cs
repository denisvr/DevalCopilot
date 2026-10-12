using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// Observes, once and read-only, the repository-local <c>remote.origin.url</c> of one already filesystem-validated project
/// root through one already-vetted direct Git executable. It admits only the main repository's own root and local
/// configuration scope, reads no network, credential, global, system, included or worktree value, and writes nothing.
/// Every unusable outcome (a refused scope, a malformed, duplicate, unsupported or oversized value, a timeout, a failure,
/// an expected launch failure) is <see cref="GitHubOriginObservation.Unavailable"/>; only the caller's own cancellation
/// is raised as <see cref="OperationCanceledException"/> and any other fault propagates.
/// </summary>
public interface IGitHubOriginObserver
{
    Task<GitHubOriginObservation> ObserveAsync(
        string gitExecutablePath, RepositoryRootCandidate candidate, CancellationToken cancellationToken);
}
