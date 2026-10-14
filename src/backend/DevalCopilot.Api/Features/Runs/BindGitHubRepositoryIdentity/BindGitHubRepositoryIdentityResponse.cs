using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

namespace DevalCopilot.Api.Features.Runs.BindGitHubRepositoryIdentity;

/// <summary>The recorded confirmation: the exact source, the target it refers to and the immutable binding. A replay of the same coherent
/// binding returns the original record unchanged and implies no current remote verification.</summary>
public sealed record BindGitHubRepositoryIdentityResponse(
    Guid RunId,
    GitHubObservationSourceResponse Source,
    GitHubObservationScopeResponse Target,
    GitHubRepositoryIdentityBindingResponse Binding);
