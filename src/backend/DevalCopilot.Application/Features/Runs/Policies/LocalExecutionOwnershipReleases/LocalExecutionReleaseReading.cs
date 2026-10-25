using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The proved reading of one release statement (ADR-0065). <see cref="Facts"/> is set exactly for Available and Released and is the
/// complete historical tuple a confirmation names; <see cref="Recorded"/> exactly for Released. NotFound, NotEligible and Unavailable
/// carry neither.
/// </summary>
internal sealed record LocalExecutionReleaseReading(
    LocalExecutionReleaseReadingKind Kind,
    LocalExecutionReleaseReason Reason,
    LocalExecutionOwnershipReleaseFacts? Facts,
    LocalExecutionReleaseRecorded? Recorded)
{
    public static LocalExecutionReleaseReading NotFound { get; } = new(LocalExecutionReleaseReadingKind.NotFound, LocalExecutionReleaseReason.None, null, null);

    public static LocalExecutionReleaseReading NotEligible(LocalExecutionReleaseReason reason) =>
        new(LocalExecutionReleaseReadingKind.NotEligible, reason, null, null);

    public static LocalExecutionReleaseReading Unavailable(LocalExecutionReleaseReason reason) =>
        new(LocalExecutionReleaseReadingKind.Unavailable, reason, null, null);

    public static LocalExecutionReleaseReading Available(LocalExecutionOwnershipReleaseFacts facts) =>
        new(LocalExecutionReleaseReadingKind.Available, LocalExecutionReleaseReason.None, facts, null);

    public static LocalExecutionReleaseReading Released(LocalExecutionOwnershipReleaseFacts facts, LocalExecutionReleaseRecorded recorded) =>
        new(LocalExecutionReleaseReadingKind.Released, LocalExecutionReleaseReason.None, facts, recorded);
}
