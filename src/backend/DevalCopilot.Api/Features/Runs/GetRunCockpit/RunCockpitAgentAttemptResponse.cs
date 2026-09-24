namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>The run's most recent Agent attempt. <c>Outcome</c> is the semantic classification;
/// <c>ProcessExecution</c> is the separate host-measured process evidence. Never a path, argument,
/// environment value, output, manifest, session identifier, or credential.</summary>
public sealed record RunCockpitAgentAttemptResponse(
    Guid AttemptId,
    int AttemptNumber,
    string? Role,
    string? Provider,
    string Status,
    string? Outcome,
    DateTimeOffset? DispatchedAtUtc,
    AgentProcessExecutionResponse ProcessExecution);
