namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// Observes, once and read-only, the repository identity, the exact branch tip and the first bounded page of check and status contexts of
/// that tip commit for one GitHub repository named by a recorded scope, through one already-vetted direct executable and the default
/// stored authentication. It proves no required-check policy, completeness or permission and never reads credentials. Every unusable
/// outcome (failure, timeout, malformed, foreign, fallback, inconsistent or oversized answer, a concurrent observation) is
/// <see langword="null"/>; only the caller's own cancellation and unexpected faults are raised.
/// </summary>
public interface IGitHubCheckContextsObserver
{
    Task<GitHubCheckContextsProbe?> ObserveAsync(
        string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken);
}
