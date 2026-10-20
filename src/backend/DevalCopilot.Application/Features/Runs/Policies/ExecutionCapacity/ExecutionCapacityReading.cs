namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>One fresh reading (ADR-0059): the closed status, the validated persisted limit (null exactly when the configuration cannot be
/// validated), the number of coherent recorded owners (null exactly when Unavailable) and the revision that set the limit. The count
/// includes the caller's own owner when a dispatch gate asked for exact self-exclusion; the gate, never the reading, subtracts it.</summary>
public sealed record ExecutionCapacityReading(ExecutionCapacityStatus Status, int? RecordedOwners, int? Limit, long? Revision);
