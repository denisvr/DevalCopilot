namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>The branch GitHub returned and the commit it pointed at: the exact short branch name and a 40-character lowercase
/// hexadecimal SHA. Provider-neutral; nothing else about the ref crosses the port.</summary>
public sealed record GitHubCiRef(string BranchName, string CommitSha);
