namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The physical termination observation of the witness, as stored. <see cref="OutcomeCode"/> is 1 Exited, 2 TimedOut, 3
/// Cancelled and 0 otherwise. Durations are typed TimeSpan values (stored as exact tick counts); exit codes are exact stored integers or null.</summary>
internal sealed record LocalExecutionReleaseObservation(
    Guid? Id,
    Guid? WitnessId,
    int OutcomeCode,
    int? SemanticExitCode,
    TimeSpan? ProcessDuration,
    int? PhysicalExitCode,
    TimeSpan? ObservedElapsed,
    DateTimeOffset? RecordedAtUtc);
