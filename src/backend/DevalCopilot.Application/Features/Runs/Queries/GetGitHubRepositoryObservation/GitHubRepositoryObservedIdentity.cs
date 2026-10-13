namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>The validated identity of an <see cref="GitHubRepositoryObservationStatus.Observed"/> answer, stamped with the host clock
/// after the recorded scope was confirmed unchanged. It is a point-in-time observation, never a binding.</summary>
public sealed record GitHubRepositoryObservedIdentity(string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc);
