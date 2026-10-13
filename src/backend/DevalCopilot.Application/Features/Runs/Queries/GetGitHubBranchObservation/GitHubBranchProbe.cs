namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>What <see cref="IGitHubBranchObserver"/> reports for one admitted answer: the opaque repository node identifier, the names
/// GitHub returned and the branch, which is <see langword="null"/> when GitHub returned no ref.</summary>
public sealed record GitHubBranchProbe(string NodeId, string Owner, string Repository, GitHubBranchRef? Ref);
