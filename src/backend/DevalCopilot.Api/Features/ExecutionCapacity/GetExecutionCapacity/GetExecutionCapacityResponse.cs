namespace DevalCopilot.Api.Features.ExecutionCapacity.GetExecutionCapacity;

/// <summary>
/// How many potentially mutating execution owners the host records across its database against the persisted admission limit, as read
/// now from existing durable rows (ADR-0058, limit per ADR-0059), and nothing else: no identifier, path, provider detail, payload,
/// timestamp, revision or request queue. <c>Status</c> is one of Available, AtCapacity and Unavailable, with exactly these tuples.
/// Available has <c>Limit</c> 1 or 2, <c>RecordedOwners</c> from 0 through limit minus one and a null <c>ReasonCode</c>; AtCapacity has
/// limit 1 or 2, a count from the limit through Int32.MaxValue and <c>execution_capacity_limit_reached</c>; Unavailable has a null count
/// and <c>execution_capacity_unavailable</c>, with the validated limit when only the ownership reading failed and a null limit exactly
/// when the configuration cannot be validated. It is never permission, liveness, remaining provider allowance or a frozen snapshot across
/// the three owner families.
/// </summary>
public sealed record GetExecutionCapacityResponse(int? Limit, int? RecordedOwners, string Status, string? ReasonCode);
