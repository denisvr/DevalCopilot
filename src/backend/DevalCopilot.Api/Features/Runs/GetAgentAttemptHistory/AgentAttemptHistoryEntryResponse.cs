namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptHistory;

/// <summary>
/// One historical Agent attempt. When <c>IdentityValid</c> is false the persisted role, provider,
/// contract, or assignment could not be proven coherent, so <c>Role</c>, <c>Provider</c>,
/// <c>ResponseContract</c>, and <c>Outcome</c> are null and the attempt is not offered for evidence.
/// </summary>
public sealed record AgentAttemptHistoryEntryResponse(
    Guid AttemptId,
    int AttemptNumber,
    string? Status,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    bool IdentityValid,
    string? Role,
    string? Provider,
    string? ResponseContract,
    string? Outcome);
