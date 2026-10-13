namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>The validated facts of a ref-bearing answer, stamped with the host clock after the recorded scope was confirmed unchanged.
/// It is a point-in-time observation, never a binding. <c>Ref</c> is <see langword="null"/> exactly for
/// <see cref="GitHubBranchObservationStatus.RefNotReturned"/>.</summary>
public sealed record GitHubBranchObserved(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, GitHubBranchRef? Ref);
