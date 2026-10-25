namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The closed outcome of proving one release statement (ADR-0065). <see cref="NotFound"/> is an absent or foreign run, sequence
/// or step (one fixed 404). <see cref="NotEligible"/> is a coherent history that does not retain a quarantine this control may release.
/// <see cref="Unavailable"/> is missing, incomplete, foreign or ambiguous evidence: the hold stays. <see cref="Available"/> carries the one
/// candidate and <see cref="Released"/> the one coherent receipt.</summary>
internal enum LocalExecutionReleaseReadingKind
{
    NotFound = 0,
    NotEligible = 1,
    Unavailable = 2,
    Available = 3,
    Released = 4,
}
