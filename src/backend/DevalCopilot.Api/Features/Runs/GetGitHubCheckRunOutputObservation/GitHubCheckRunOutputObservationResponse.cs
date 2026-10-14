namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>
/// The opaque repository node identifier and owner and repository names GitHub returned (equal to the recorded scope ignoring only ASCII
/// case), the host's observation time, the returned branch commit (null when GitHub returned no ref) and the admitted check run.
/// <c>CheckRun</c> is null unless the returned commit equals the recorded commit and the requested check run was proved to belong to the
/// bound repository and that commit in both phases.
/// </summary>
public sealed record GitHubCheckRunOutputObservationResponse(
    string RepositoryNodeId,
    string Owner,
    string Repository,
    DateTimeOffset ObservedAtUtc,
    GitHubCheckRunOutputRefResponse? Ref,
    GitHubCheckRunOutputCheckRunResponse? CheckRun);
