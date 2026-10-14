namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

/// <summary>One of NotRecorded, Unavailable or Available. <c>Source</c> and <c>Target</c> are present for Available and, when the target is
/// coherent, for NotRecorded; <c>Binding</c> only for Available. Unavailable carries none and never says why. A recorded binding is
/// historical evidence, never a fresh remote check, permission, CI success or publication authority.</summary>
public sealed record GetGitHubRepositoryIdentityBindingResponse(
    Guid RunId,
    string State,
    GitHubObservationSourceResponse? Source,
    GitHubObservationScopeResponse? Target,
    GitHubRepositoryIdentityBindingResponse? Binding);
