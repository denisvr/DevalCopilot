using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;

/// <summary>The recorded confirmation: the exact source, the target it refers to and the immutable binding. A replay of the same coherent
/// binding returns the original identifiers and times unchanged and implies no current remote verification.</summary>
public sealed record BindGitHubRepositoryIdentityCommandResult(
    Guid RunId,
    GitHubObservationSourceView Source,
    GitHubObservationTargetView Target,
    GitHubRepositoryIdentityBindingView Binding);
