namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>What <see cref="IGitHubCiObserver"/> reports for one admitted answer: the opaque repository node identifier, the names GitHub
/// returned, the exact branch commit (<see langword="null"/> when GitHub returned no ref) and GitHub's literal rollup state for that commit
/// (<see langword="null"/> when GitHub returned no rollup, or no ref).</summary>
public sealed record GitHubCiProbe(string NodeId, string Owner, string Repository, GitHubCiRef? Ref, string? RollupState);
