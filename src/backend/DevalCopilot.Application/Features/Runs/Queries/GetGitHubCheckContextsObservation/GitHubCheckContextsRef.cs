namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>The branch GitHub returned and the commit it pointed at: the exact short branch name and a 40-character lowercase
/// hexadecimal SHA. Provider-neutral; nothing else about the ref crosses the port.</summary>
public sealed record GitHubCheckContextsRef(string BranchName, string CommitSha);
