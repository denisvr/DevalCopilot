namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>What <see cref="IGitHubCheckContextsObserver"/> reports for one admitted answer: the opaque repository node identifier, the
/// names GitHub returned, the exact branch commit (<see langword="null"/> when GitHub returned no ref) and, only beside a ref, the first
/// bounded page of contexts (<see langword="null"/> when GitHub returned no rollup).</summary>
public sealed record GitHubCheckContextsProbe(
    string NodeId, string Owner, string Repository, GitHubCheckContextsRef? Ref, GitHubCheckContextsPage? Page);
