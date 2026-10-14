using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// <see cref="GitHubCheckContextsObservationStatus.NotRecorded"/> and <see cref="GitHubCheckContextsObservationStatus.Unavailable"/> carry
/// no fact; <see cref="GitHubCheckContextsObservationStatus.BindingNotRecorded"/> carries the coherent source and target only; every other
/// status carries the coherent source, target and binding, and only the final five statuses an observation.
/// </summary>
public sealed record GetGitHubCheckContextsObservationQueryResult(
    Guid RunId,
    GitHubCheckContextsObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding,
    GitHubCheckContextsObserved? Observation);
