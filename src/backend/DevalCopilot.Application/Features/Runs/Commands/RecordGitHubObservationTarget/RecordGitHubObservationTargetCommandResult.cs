namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;

/// <summary>The recorded selection: the exact source and the immutable target. A replay of the same coherent selection returns the
/// original identifiers and time unchanged.</summary>
public sealed record RecordGitHubObservationTargetCommandResult(
    Guid RunId, GitHubObservationSourceView Source, GitHubObservationTargetView Target);
