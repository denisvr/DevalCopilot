namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>What the persisted rows say about a run's identity binding: nothing at all, one coherent version-1 record validated together with
/// its source, target and single Human event, or something that must never be treated as a record.</summary>
internal sealed record GitHubRepositoryIdentityBindingReading(
    GitHubRepositoryIdentityBindingReadingKind Kind, GitHubRepositoryIdentityBindingView? Binding)
{
    public static readonly GitHubRepositoryIdentityBindingReading None = new(GitHubRepositoryIdentityBindingReadingKind.None, null);

    public static readonly GitHubRepositoryIdentityBindingReading Incoherent = new(GitHubRepositoryIdentityBindingReadingKind.Incoherent, null);
}
