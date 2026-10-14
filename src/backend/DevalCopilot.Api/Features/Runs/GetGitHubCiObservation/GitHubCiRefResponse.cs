namespace DevalCopilot.Api.Features.Runs.GetGitHubCiObservation;

/// <summary>The exact short branch name GitHub returned and the 40-character lowercase hexadecimal commit it pointed at.</summary>
public sealed record GitHubCiRefResponse(string BranchName, string CommitSha);
