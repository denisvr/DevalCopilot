using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

namespace DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;

/// <summary>The closed tuple of ADR-0059: Available has limit 1 or 2, a recorded count from 0 through limit minus one and no reason;
/// AtCapacity has limit 1 or 2, a count from the limit through Int32.MaxValue and <c>execution_capacity_limit_reached</c>; Unavailable has
/// no count and <c>execution_capacity_unavailable</c>, with the validated limit when only the ownership reading failed and a null limit
/// exactly when the configuration cannot be validated.</summary>
public sealed record GetExecutionCapacityQueryResult(ExecutionCapacityStatus Status, int? Limit, int? RecordedOwners, string? ReasonCode);
