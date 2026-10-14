namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Observes, once and read-only, the identity of one GitHub repository named by a recorded scope, through one already-vetted direct
/// executable and the default stored authentication. It proves no branch, commit, permission or CI result and never reads
/// credentials. Every unusable outcome (failure, timeout, malformed, foreign or oversized answer, a concurrent probe) is
/// <see langword="null"/>; only the caller's own cancellation and unexpected faults are raised.
/// </summary>
public interface IGitHubRepositoryIdentityObserver
{
    Task<GitHubRepositoryIdentity?> ObserveAsync(
        string executablePath, string owner, string repository, CancellationToken cancellationToken);
}
