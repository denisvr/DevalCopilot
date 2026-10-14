namespace DevalCopilot.Api.Features.Runs.GetGitHubCiObservation;

/// <summary>The opaque repository node identifier and owner and repository names GitHub returned (equal to the recorded scope ignoring only
/// ASCII case), the host's observation time, the returned branch commit (null when GitHub returned no ref) and GitHub's literal rollup
/// state, which is non-null only when the returned commit equals the recorded commit and GitHub reported a rollup. A null state means
/// GitHub returned no rollup for that commit, or none was exposed; it never means that no checks exist.</summary>
public sealed record GitHubCiObservationResponse(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, GitHubCiRefResponse? Ref, string? RollupState);
