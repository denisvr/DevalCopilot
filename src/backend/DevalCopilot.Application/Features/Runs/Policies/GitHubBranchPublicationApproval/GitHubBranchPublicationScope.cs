using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>The complete coherent scope an approval is recorded against: the pinned source, the recorded target and the recorded binding.
/// Record equality compares every value of all three, so a scope read twice is the same scope only when nothing in it changed.</summary>
internal sealed record GitHubBranchPublicationScope(
    GitHubObservationSource Source, GitHubObservationTargetView Target, GitHubRepositoryIdentityBindingView Binding);
