using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The recorded release inspection of one statement (ADR-0067). <see cref="Facts"/> and <see cref="Recorded"/> are set exactly
/// for Recorded.</summary>
internal sealed record LocalExecutionReleaseInspectionReading(
    LocalExecutionReleaseInspectionReadingKind Kind,
    LocalExecutionOwnershipReleaseFacts? Facts,
    LocalExecutionReleaseRecorded? Recorded)
{
    public static LocalExecutionReleaseInspectionReading NotFound { get; } = new(LocalExecutionReleaseInspectionReadingKind.NotFound, null, null);

    public static LocalExecutionReleaseInspectionReading NotRecorded { get; } = new(LocalExecutionReleaseInspectionReadingKind.NotRecorded, null, null);

    public static LocalExecutionReleaseInspectionReading Unavailable { get; } = new(LocalExecutionReleaseInspectionReadingKind.Unavailable, null, null);
}
