using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>
/// <see cref="GitHubCiObservationStatus.NotRecorded"/> and <see cref="GitHubCiObservationStatus.Unavailable"/> carry no fact;
/// <see cref="GitHubCiObservationStatus.BindingNotRecorded"/> carries the coherent source and target only; every other status carries the
/// coherent source, target and binding, and only <see cref="GitHubCiObservationStatus.RefNotReturned"/>,
/// <see cref="GitHubCiObservationStatus.CommitDifferent"/>, <see cref="GitHubCiObservationStatus.NoStatusReported"/> and
/// <see cref="GitHubCiObservationStatus.StatusReported"/> an observation.
/// </summary>
public sealed record GetGitHubCiObservationQueryResult(
    Guid RunId,
    GitHubCiObservationStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding,
    GitHubCiObserved? Observation);
