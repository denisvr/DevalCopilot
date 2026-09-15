namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointDiff;

public sealed record GetGitCheckpointDiffResponse(string FingerprintSha256, string CompleteDiff);
