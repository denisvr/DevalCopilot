using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckContextsObservation;

/// <summary>
/// One of NotRecorded, Unavailable, BindingNotRecorded, CliUnavailable, Unknown, IdentityChanged, RefNotReturned, CommitDifferent,
/// NoContextsReturned, LimitExceeded or ContextsReported. NotRecorded and Unavailable carry no <c>Source</c>, <c>Target</c>,
/// <c>Binding</c> or <c>Observation</c>; BindingNotRecorded carries only the coherent <c>Source</c> and <c>Target</c>; CliUnavailable,
/// Unknown and IdentityChanged add the immutable <c>Binding</c>; only the final five statuses add an <c>Observation</c>. It is the bounded
/// collection GitHub returned for one commit: never complete check or workflow evidence, a required-check approval, a permission or
/// publication authority.
/// </summary>
public sealed record GetGitHubCheckContextsObservationResponse(
    Guid RunId,
    string Status,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubRepositoryIdentityBindingResponse? Binding,
    GitHubCheckContextsObservationResponse? Observation);
