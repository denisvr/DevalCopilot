namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>One fresh reading: the closed status and the number of coherent recorded owners (null exactly when Unavailable). The count
/// includes the caller's own owner when a dispatch gate asked for exact self-exclusion; the gate, never the reading, subtracts it.</summary>
public sealed record ExecutionCapacityReading(ExecutionCapacityStatus Status, int? RecordedOwners);
