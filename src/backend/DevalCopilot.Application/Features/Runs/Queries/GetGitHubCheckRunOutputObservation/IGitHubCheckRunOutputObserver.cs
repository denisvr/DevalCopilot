namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// Observes, read-only and through one already-vetted direct executable with the default stored authentication, the repository identity,
/// the exact branch tip and one global node by identifier, in two separate phases. The metadata phase selects the association of a check
/// run and no output; the output phase repeats that association and adds the name, status, conclusion and output text. The observer keeps
/// no state between phases and never decides whether the output phase may run: the host admits that. It proves no required-check policy,
/// completeness or permission and never reads credentials. Every unusable outcome (failure, timeout, malformed, foreign, fallback,
/// inconsistent or oversized answer, a concurrent observation) is <see langword="null"/>; only the caller's own cancellation and
/// unexpected faults are raised.
/// </summary>
public interface IGitHubCheckRunOutputObserver
{
    Task<GitHubCheckRunProbe?> ObserveMetadataAsync(
        string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken);

    Task<GitHubCheckRunProbe?> ObserveOutputAsync(
        string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken);
}
