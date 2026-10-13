namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchObservation;

/// <summary>The exact short branch name GitHub returned and the 40-character lowercase hexadecimal commit it pointed at.</summary>
public sealed record GitHubBranchRefResponse(string BranchName, string CommitSha);
