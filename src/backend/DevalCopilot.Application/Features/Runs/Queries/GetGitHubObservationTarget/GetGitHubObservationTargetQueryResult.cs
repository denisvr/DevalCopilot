namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubObservationTarget;

/// <summary><see cref="GitHubObservationTargetState.NotRecorded"/> carries no target and a source only when it is admissible;
/// <see cref="GitHubObservationTargetState.Available"/> carries both, validated together; <see cref="GitHubObservationTargetState.Unavailable"/>
/// carries neither.</summary>
public sealed record GetGitHubObservationTargetQueryResult(
    Guid RunId,
    GitHubObservationTargetState State,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target);
