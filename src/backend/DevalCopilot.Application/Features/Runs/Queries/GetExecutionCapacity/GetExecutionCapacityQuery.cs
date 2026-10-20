using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;

/// <summary>
/// How many potentially mutating execution owners are recorded across the authoritative database, read now from existing durable rows
/// through the shared capacity policy (ADR-0058). It reads no Git, provider, process table or AgentSupervision, writes nothing, and states
/// only what this read found: never permission, liveness, remaining provider allowance or a frozen cross-family snapshot.
/// </summary>
public sealed record GetExecutionCapacityQuery : IQuery<Result<GetExecutionCapacityQueryResult>>;
