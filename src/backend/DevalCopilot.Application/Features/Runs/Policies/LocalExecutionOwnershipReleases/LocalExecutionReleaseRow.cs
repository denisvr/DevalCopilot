namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>One row of the single release statement (ADR-0065): the root, the selected step, its witness, observation and release, and
/// the linked operation with its admission, and whether every NULLABLE numeric member read is genuinely NULL or exactly a supported integer
/// (<see cref="StorageValid"/>, computed from the raw storage in the same statement). At most two rows exist (two roots), and more than one is an ambiguous root.</summary>
internal sealed record LocalExecutionReleaseRow(
    LocalExecutionReleaseRoot Root,
    LocalExecutionReleaseStep Step,
    LocalExecutionReleaseWitness Witness,
    LocalExecutionReleaseObservation Observation,
    LocalExecutionReleaseReceipt Receipt,
    LocalExecutionReleaseOperation Operation,
    bool StorageValid);
