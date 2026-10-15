using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// <see cref="GitHubRepositoryPermissionObservationStatus.NotRecorded"/> and <see cref="GitHubRepositoryPermissionObservationStatus.Unavailable"/>
/// carry no fact; <see cref="GitHubRepositoryPermissionObservationStatus.BindingNotRecorded"/> carries the coherent source and target only;
/// <see cref="GitHubRepositoryPermissionObservationStatus.CliUnavailable"/>, <see cref="GitHubRepositoryPermissionObservationStatus.Unknown"/>
/// and <see cref="GitHubRepositoryPermissionObservationStatus.IdentityChanged"/> carry the coherent source, target and binding and no
/// observation; <see cref="GitHubRepositoryPermissionObservationStatus.PermissionNotReturned"/> and
/// <see cref="GitHubRepositoryPermissionObservationStatus.PermissionReported"/> add the observation.
/// </summary>
public sealed record GetGitHubRepositoryPermissionObservationQueryResult(
    Guid RunId,
    GitHubRepositoryPermissionObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding,
    GitHubRepositoryPermissionObserved? Observation);
