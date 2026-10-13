namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchObservation;

/// <summary>
/// One of NotRecorded, Unavailable, CliUnavailable, Unknown, RefNotReturned, CommitMatched or CommitDifferent. NotRecorded and
/// Unavailable carry no <c>Source</c>, <c>Target</c> or <c>Observation</c>; every other status carries the exact recorded source and
/// target, and only RefNotReturned, CommitMatched and CommitDifferent an <c>Observation</c> (whose <c>Ref</c> is null exactly for
/// RefNotReturned). It is a point-in-time observation: never a CI result, permission, durable binding or publication approval.
/// </summary>
public sealed record GetGitHubBranchObservationResponse(
    Guid RunId,
    string Status,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubBranchObservationResponse? Observation);
