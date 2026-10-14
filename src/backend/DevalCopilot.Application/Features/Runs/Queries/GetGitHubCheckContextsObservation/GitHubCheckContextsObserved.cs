namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>The validated facts of an admitted answer, stamped with the host clock after the recorded scope and binding were confirmed
/// unchanged. It is a point-in-time observation, never a binding or an approval. <c>Ref</c> is <see langword="null"/> exactly for
/// <see cref="GitHubCheckContextsObservationStatus.RefNotReturned"/>; <c>TotalCount</c> is non-null exactly for
/// <see cref="GitHubCheckContextsObservationStatus.LimitExceeded"/> and <see cref="GitHubCheckContextsObservationStatus.ContextsReported"/>
/// and <c>Contexts</c> exactly for the latter.</summary>
public sealed record GitHubCheckContextsObserved(
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    GitHubCheckContextsRef? Ref,
    int? TotalCount,
    IReadOnlyList<GitHubCheckContext>? Contexts);
