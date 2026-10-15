using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>Everything the unchanged source, target and binding readers reported, kept apart so absence and incoherence stay distinct;
/// <see cref="Scope"/> exists only when all three are present and coherent.</summary>
internal sealed record GitHubBranchPublicationScopeReading(
    GitHubObservationSource? Source,
    GitHubObservationTargetReading Target,
    GitHubRepositoryIdentityBindingReading Binding)
{
    public GitHubObservationTargetView? CoherentTarget =>
        Target.Kind == GitHubObservationTargetReadingKind.Coherent ? Target.Target : null;

    public GitHubRepositoryIdentityBindingView? CoherentBinding =>
        Binding.Kind == GitHubRepositoryIdentityBindingReadingKind.Coherent ? Binding.Binding : null;

    public bool IsIncoherent =>
        Target.Kind == GitHubObservationTargetReadingKind.Incoherent
        || Binding.Kind == GitHubRepositoryIdentityBindingReadingKind.Incoherent;

    public GitHubBranchPublicationScope? Scope =>
        Source is not null && CoherentTarget is { } target && CoherentBinding is { } binding
            ? new GitHubBranchPublicationScope(Source, target, binding)
            : null;
}
