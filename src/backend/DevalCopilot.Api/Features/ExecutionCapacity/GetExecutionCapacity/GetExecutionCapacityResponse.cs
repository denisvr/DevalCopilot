namespace DevalCopilot.Api.Features.ExecutionCapacity.GetExecutionCapacity;

/// <summary>
/// How many potentially mutating execution owners the host records across its database, as read now from existing durable rows
/// (ADR-0058), and nothing else: no identifier, path, provider detail, payload, timestamp or request queue. <c>Status</c> is one of
/// Available, AtCapacity and Unavailable, with exactly these tuples. Available has <c>Limit</c> 2, <c>RecordedOwners</c> 0 or 1 and a null
/// <c>ReasonCode</c>; AtCapacity has limit 2, a count from 2 through Int32.MaxValue and <c>execution_capacity_limit_reached</c>;
/// Unavailable has limit 2, a null count and <c>execution_capacity_unavailable</c>. It is never permission, liveness, remaining provider
/// allowance or a frozen snapshot across the three owner families.
/// </summary>
public sealed record GetExecutionCapacityResponse(int Limit, int? RecordedOwners, string Status, string? ReasonCode);
