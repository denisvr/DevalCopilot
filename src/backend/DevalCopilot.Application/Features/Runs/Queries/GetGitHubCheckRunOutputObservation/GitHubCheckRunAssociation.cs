namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>The complete identity by which GitHub associated one check run with a repository and a commit: the check run identifier and
/// its repository, the check suite identifier and its repository, and the commit of that suite. Two associations are the same only when
/// every value is ordinally equal.</summary>
public sealed record GitHubCheckRunAssociation(
    string CheckRunId, string CheckRunRepositoryId, string CheckSuiteId, string CheckSuiteRepositoryId, string CommitSha);
