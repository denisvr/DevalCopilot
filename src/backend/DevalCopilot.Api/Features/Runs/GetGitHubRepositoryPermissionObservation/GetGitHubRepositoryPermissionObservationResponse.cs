using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// One of NotRecorded, Unavailable, BindingNotRecorded, CliUnavailable, Unknown, IdentityChanged, PermissionNotReturned or
/// PermissionReported. NotRecorded and Unavailable carry no <c>Source</c>, <c>Target</c>, <c>Binding</c> or <c>Observation</c>;
/// BindingNotRecorded carries only the coherent <c>Source</c> and <c>Target</c>; CliUnavailable, Unknown and IdentityChanged add the
/// immutable <c>Binding</c>; only PermissionNotReturned and PermissionReported add an <c>Observation</c>. It is the repository access GitHub
/// reported at one moment: never permission to push a branch, readiness, an approval or a publication authority.
/// </summary>
public sealed record GetGitHubRepositoryPermissionObservationResponse(
    Guid RunId,
    string Status,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubRepositoryIdentityBindingResponse? Binding,
    GitHubRepositoryPermissionObservationResponse? Observation);
