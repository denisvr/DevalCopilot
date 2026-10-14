namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>What <see cref="IGitHubCheckRunOutputObserver"/> reports for one admitted answer: the opaque repository node identifier, the
/// names GitHub returned, the exact branch commit (<see langword="null"/> when GitHub returned no ref) and the node GitHub returned for
/// the requested check run identifier (<see langword="null"/> when it returned none).</summary>
public sealed record GitHubCheckRunProbe(
    string NodeId, string Owner, string Repository, GitHubCheckRunOutputRef? Ref, GitHubCheckRunNode? Node);
