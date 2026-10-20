namespace DevalCopilot.Api.Features.ExecutionCapacity.SetExecutionCapacityLimit;

/// <summary>
/// The capacity reading under the committed (or unchanged) limit (ADR-0059), with exactly the four members and closed tuples of the
/// capacity read: Available has limit 1 or 2, a count from 0 through limit minus one and no reason; AtCapacity has limit 1 or 2, a count
/// from the limit through Int32.MaxValue and <c>execution_capacity_limit_reached</c>; Unavailable has a null count and
/// <c>execution_capacity_unavailable</c>. It is an advisory observation made under the write boundary, never a future grant.
/// </summary>
public sealed record SetExecutionCapacityLimitResponse(int? Limit, int? RecordedOwners, string Status, string? ReasonCode);
