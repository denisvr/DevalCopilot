namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckContextsObservation;

/// <summary>
/// The opaque repository node identifier and owner and repository names GitHub returned (equal to the recorded scope ignoring only ASCII
/// case), the host's observation time, the returned branch commit (null when GitHub returned no ref), the total count of contexts and the
/// contexts themselves. <c>TotalCount</c> and <c>Contexts</c> are both null unless the returned commit equals the recorded commit and
/// GitHub returned a rollup; <c>Contexts</c> is additionally null when the total exceeds the 50-entry bound. A null collection means no
/// rollup was exposed or the bound was exceeded; an empty array means GitHub returned zero entries. Neither means success.
/// </summary>
public sealed record GitHubCheckContextsObservationResponse(
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    GitHubCheckContextsRefResponse? Ref,
    int? TotalCount,
    IReadOnlyList<GitHubCheckContextResponse>? Contexts);
