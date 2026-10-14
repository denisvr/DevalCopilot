namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>The exact short branch name GitHub returned and the 40-character lowercase hexadecimal commit it pointed at.</summary>
public sealed record GitHubCheckRunOutputRefResponse(string BranchName, string CommitSha);
