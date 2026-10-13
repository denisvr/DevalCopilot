namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>The branch GitHub returned and the commit it pointed at: the exact short branch name and a 40-character lowercase
/// hexadecimal SHA. Provider-neutral; nothing else about the ref crosses the port.</summary>
public sealed record GitHubBranchRef(string BranchName, string CommitSha);
