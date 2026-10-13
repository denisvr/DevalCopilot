namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryObservation;

/// <summary>
/// One of NotRecorded, Unavailable, CliUnavailable, Unknown or Observed. NotRecorded and Unavailable carry no <c>Source</c>, <c>Target</c>
/// or <c>Observation</c>; every other status carries the exact recorded source and target, and only Observed an <c>Observation</c>. It is
/// a point-in-time observation of the repository's ID and names: never a remote branch, commit, permission, CI result, publication
/// approval or permanent binding.
/// </summary>
public sealed record GetGitHubRepositoryObservationResponse(
    Guid RunId,
    string Status,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubRepositoryIdentityResponse? Observation);
