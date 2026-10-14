using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>
/// One of NotRecorded, Unavailable, BindingNotRecorded, CliUnavailable, Unknown, IdentityChanged, RefNotReturned, CommitDifferent,
/// CheckRunUnavailable, OutputNotReturned or OutputReported. <c>CheckRunId</c> echoes the validated request. NotRecorded and Unavailable
/// carry no <c>Source</c>, <c>Target</c>, <c>Binding</c> or <c>Observation</c>; BindingNotRecorded carries only the coherent
/// <c>Source</c> and <c>Target</c>; CliUnavailable, Unknown and IdentityChanged add the immutable <c>Binding</c>; only the final five
/// statuses add an <c>Observation</c>. It is untrusted, post-redaction provider text for one check run at one moment: never complete
/// check or workflow evidence, a required-check approval, a diagnosis, a permission or publication authority.
/// </summary>
public sealed record GetGitHubCheckRunOutputObservationResponse(
    Guid RunId,
    string CheckRunId,
    string Status,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubRepositoryIdentityBindingResponse? Binding,
    GitHubCheckRunOutputObservationResponse? Observation);
