namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>What the persisted rows say about a run's recorded target: nothing at all, one coherent version-1 record validated
/// together with its source and its single Human event, or something that must never be treated as a record.</summary>
internal sealed record GitHubObservationTargetReading(GitHubObservationTargetReadingKind Kind, GitHubObservationTargetView? Target)
{
    public static readonly GitHubObservationTargetReading None = new(GitHubObservationTargetReadingKind.None, null);

    public static readonly GitHubObservationTargetReading Incoherent = new(GitHubObservationTargetReadingKind.Incoherent, null);
}
