namespace DevalCopilot.Api.Features.Runs.RecordGitHubObservationTarget;

/// <summary>The recorded selection: the exact source and the immutable target. A replay of the same coherent selection returns the
/// original record unchanged. It verifies nothing remote and grants no publication or automatic observation authority.</summary>
public sealed record RecordGitHubObservationTargetResponse(
    Guid RunId, GitHubObservationSourceResponse Source, GitHubObservationScopeResponse Target);
