namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

/// <summary>A tracked changed path without comparison text and its fixed reason code. Neither is ever repository text beyond the
/// path Git reported, an exception message or a filesystem path outside the workspace.</summary>
public sealed record GetGitCheckpointDiffOmission(string Path, string Reason);
