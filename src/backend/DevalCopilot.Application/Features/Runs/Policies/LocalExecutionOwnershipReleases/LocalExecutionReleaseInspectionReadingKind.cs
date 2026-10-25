namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The closed outcome of one recorded release inspection (ADR-0067). <see cref="NotFound"/> is an absent or foreign run, sequence
/// or step. <see cref="Recorded"/> is a fully proved release. <see cref="NotRecorded"/> is a supported proof result with both receipt
/// associations absent. <see cref="Unavailable"/> is everything else.</summary>
internal enum LocalExecutionReleaseInspectionReadingKind
{
    NotFound = 0,
    NotRecorded = 1,
    Unavailable = 2,
    Recorded = 3,
}
