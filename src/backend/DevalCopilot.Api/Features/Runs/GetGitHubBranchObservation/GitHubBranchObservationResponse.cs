namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchObservation;

/// <summary>The opaque repository node identifier and owner and repository names GitHub returned (equal to the recorded scope ignoring
/// only ASCII case), the host's observation time and the returned branch, which is null when GitHub returned no ref.</summary>
public sealed record GitHubBranchObservationResponse(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, GitHubBranchRefResponse? Ref);
