namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointDiff;

/// <summary>A tracked changed path without comparison text and its fixed reason code.</summary>
public sealed record GetGitCheckpointDiffOmissionResponse(string Path, string Reason);
