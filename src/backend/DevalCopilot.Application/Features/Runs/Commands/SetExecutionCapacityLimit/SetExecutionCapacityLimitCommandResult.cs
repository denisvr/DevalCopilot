using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

namespace DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;

/// <summary>The reading under the committed (or unchanged) limit, projected under the write boundary. It is an advisory observation, not a
/// future grant; its tuple is the closed one of the capacity read.</summary>
public sealed record SetExecutionCapacityLimitCommandResult(ExecutionCapacityStatus Status, int? Limit, int? RecordedOwners, string? ReasonCode);
