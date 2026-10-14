using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// <see cref="GitHubCheckRunOutputObservationStatus.NotRecorded"/> and <see cref="GitHubCheckRunOutputObservationStatus.Unavailable"/>
/// carry no fact; <see cref="GitHubCheckRunOutputObservationStatus.BindingNotRecorded"/> carries the coherent source and target only;
/// every other status carries the coherent source, target and binding, and only the final five statuses an observation.
/// </summary>
public sealed record GetGitHubCheckRunOutputObservationQueryResult(
    Guid RunId,
    string CheckRunId,
    GitHubCheckRunOutputObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding,
    GitHubCheckRunOutputObserved? Observation);
