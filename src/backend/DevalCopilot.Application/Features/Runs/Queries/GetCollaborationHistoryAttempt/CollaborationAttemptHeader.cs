using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>The minimal recorded header of the Agent attempt a provider-observed message references: identity, number, the coherent
/// role, provider and response contract, the recorded status and the recorded times. It states recorded attribution only, never
/// current execution, outcome, authority, provider capability or sealed evidence.</summary>
public sealed record CollaborationAttemptHeader(
    Guid Id,
    int AttemptNumber,
    AgentRole Role,
    AgentProvider Provider,
    AgentResponseContract ResponseContract,
    AttemptStatus Status,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
