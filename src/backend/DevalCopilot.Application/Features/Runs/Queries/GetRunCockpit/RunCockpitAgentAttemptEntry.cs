using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>The run's most recent Agent attempt, bounded to identity, role/provider provenance, the
/// semantic <see cref="Outcome"/>, and — as separate siblings — the host-measured
/// <see cref="ProcessExecution"/> evidence and configured <see cref="Timeout"/>, and the
/// provider-reported <see cref="TokenUsage"/> evidence. Never a path, argument, environment value,
/// output, manifest, session identifier, or credential.</summary>
public sealed record RunCockpitAgentAttemptEntry(
    Guid AttemptId,
    int AttemptNumber,
    AgentRole? Role,
    AgentProvider? Provider,
    AttemptStatus Status,
    AgentOutcome? Outcome,
    DateTimeOffset? DispatchedAtUtc,
    AgentProcessExecutionEvidence? ProcessExecution,
    TimeSpan? Timeout,
    AgentTokenUsageEvidence? TokenUsage = null);
