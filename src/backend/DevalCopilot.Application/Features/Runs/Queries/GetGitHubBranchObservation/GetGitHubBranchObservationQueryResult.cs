namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// <see cref="GitHubBranchObservationStatus.NotRecorded"/> and <see cref="GitHubBranchObservationStatus.Unavailable"/> carry no scope
/// and no observation; every other status carries the exact coherent local source and target, and only
/// <see cref="GitHubBranchObservationStatus.RefNotReturned"/>, <see cref="GitHubBranchObservationStatus.CommitMatched"/> and
/// <see cref="GitHubBranchObservationStatus.CommitDifferent"/> an observation.
/// </summary>
public sealed record GetGitHubBranchObservationQueryResult(
    Guid RunId,
    GitHubBranchObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubBranchObserved? Observation);
