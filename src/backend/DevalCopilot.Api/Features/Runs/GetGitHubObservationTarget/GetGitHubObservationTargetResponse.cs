namespace DevalCopilot.Api.Features.Runs.GetGitHubObservationTarget;

/// <summary>One of NotRecorded, Unavailable or Available. <c>Source</c> is present for Available and, when admissible, for NotRecorded;
/// <c>Target</c> only for Available. Unavailable carries neither and never says why. It is recorded local scope selection, never
/// remote verification, permission, CI success or publication authority.</summary>
public sealed record GetGitHubObservationTargetResponse(
    Guid RunId, string State, GitHubObservationSourceResponse? Source, GitHubObservationScopeResponse? Target);
