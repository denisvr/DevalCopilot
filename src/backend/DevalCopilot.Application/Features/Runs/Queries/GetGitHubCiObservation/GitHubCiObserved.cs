namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>The validated facts of an admitted answer, stamped with the host clock after the recorded scope and binding were confirmed
/// unchanged. It is a point-in-time observation, never a binding or an approval. <c>Ref</c> is <see langword="null"/> exactly for
/// <see cref="GitHubCiObservationStatus.RefNotReturned"/>; <c>RollupState</c> is non-null exactly for
/// <see cref="GitHubCiObservationStatus.StatusReported"/>.</summary>
public sealed record GitHubCiObserved(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, GitHubCiRef? Ref, string? RollupState);
