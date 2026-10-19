using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>The selected Agent attempt exactly as recorded: identity, contract and its real status, outcome and dispatch/completion facts.
/// <c>Dispatched</c> says only that the single-use marker exists; it is not proof that a process runs.</summary>
public sealed record AgentSupervisionAttemptView(
    Guid AttemptId,
    int AttemptNumber,
    AgentRole Role,
    AgentProvider Provider,
    AgentResponseContract ResponseContract,
    AttemptStatus Status,
    AgentOutcome? Outcome,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
