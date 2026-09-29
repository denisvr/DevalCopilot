using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;

/// <summary>
/// One historical Agent attempt row, bounded to identity and lifecycle facts. When
/// <see cref="IdentityValid"/> is <see langword="false"/> the persisted role, provider, contract, or
/// assignment could not be proven coherent, so role, provider, response contract, and outcome are
/// withheld (null) and the row is not offered for evidence. Never carries a storage path, content
/// hash, provider session identifier, prompt, or any artifact content.
/// </summary>
public sealed record AgentAttemptHistoryEntry(
    Guid AttemptId,
    int AttemptNumber,
    AttemptStatus? Status,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    bool IdentityValid,
    AgentRole? Role,
    AgentProvider? Provider,
    AgentResponseContract? ResponseContract,
    AgentOutcome? Outcome);
