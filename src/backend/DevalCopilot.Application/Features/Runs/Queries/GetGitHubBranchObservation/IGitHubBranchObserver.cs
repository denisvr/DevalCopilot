namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// Observes, once and read-only, the repository identity and the exact branch tip of one GitHub repository named by a recorded scope,
/// through one already-vetted direct executable and the default stored authentication. It proves no CI result or permission and never
/// reads credentials. Every unusable outcome (failure, timeout, malformed, foreign, fallback or oversized answer, a concurrent
/// observation) is <see langword="null"/>; only the caller's own cancellation and unexpected faults are raised.
/// </summary>
public interface IGitHubBranchObserver
{
    Task<GitHubBranchProbe?> ObserveAsync(
        string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken);
}
