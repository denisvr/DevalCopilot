namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryObservation;

/// <summary>The opaque repository node identifier and the owner and repository names GitHub returned, with the host's observation time.
/// The names equal the recorded scope ignoring only ASCII case.</summary>
public sealed record GitHubRepositoryIdentityResponse(string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc);
