using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;

/// <summary><see cref="GitHubRepositoryIdentityBindingState.NotRecorded"/> carries no binding and the source and target pair only when the
/// target is coherent; <see cref="GitHubRepositoryIdentityBindingState.Available"/> carries all three, validated together;
/// <see cref="GitHubRepositoryIdentityBindingState.Unavailable"/> carries none, including for orphan binding rows or events.</summary>
public sealed record GetGitHubRepositoryIdentityBindingQueryResult(
    Guid RunId,
    GitHubRepositoryIdentityBindingState State,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding);
