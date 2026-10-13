namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>
/// <see cref="GitHubRepositoryObservationStatus.NotRecorded"/> and <see cref="GitHubRepositoryObservationStatus.Unavailable"/> carry
/// no scope and no observation; every other status carries the exact coherent local source and target, and only
/// <see cref="GitHubRepositoryObservationStatus.Observed"/> an identity.
/// </summary>
public sealed record GetGitHubRepositoryObservationQueryResult(
    Guid RunId,
    GitHubRepositoryObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryObservedIdentity? Observation);
