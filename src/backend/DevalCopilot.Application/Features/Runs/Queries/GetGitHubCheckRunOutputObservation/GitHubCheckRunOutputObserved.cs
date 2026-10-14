namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>The validated facts of an admitted answer, stamped with the host clock after the last external call and after the recorded
/// scope and binding were confirmed unchanged. It is a point-in-time observation, never a binding or an approval. <c>Ref</c> is
/// <see langword="null"/> exactly for <see cref="GitHubCheckRunOutputObservationStatus.RefNotReturned"/>; <c>CheckRun</c> is non-null
/// exactly for <see cref="GitHubCheckRunOutputObservationStatus.OutputNotReturned"/> and
/// <see cref="GitHubCheckRunOutputObservationStatus.OutputReported"/>.</summary>
public sealed record GitHubCheckRunOutputObserved(
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    GitHubCheckRunOutputRef? Ref,
    GitHubCheckRunOutputCheckRun? CheckRun);
