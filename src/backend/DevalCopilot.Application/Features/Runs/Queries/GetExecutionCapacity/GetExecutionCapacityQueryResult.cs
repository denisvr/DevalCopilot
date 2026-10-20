using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

namespace DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;

/// <summary>The closed tuple of ADR-0058: Available has limit 2, a recorded count of 0 or 1 and no reason; AtCapacity has limit 2, a count
/// of 2 or more and <c>execution_capacity_limit_reached</c>; Unavailable has limit 2, no count and <c>execution_capacity_unavailable</c>.</summary>
public sealed record GetExecutionCapacityQueryResult(ExecutionCapacityStatus Status, int Limit, int? RecordedOwners, string? ReasonCode);
