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
    string? Outcome,
    /// <summary>The attempt this attempt is the one manual format repair of when that link can be proved (an
    /// earlier Agent attempt of this run), else null. Lineage only, never a claim that the source was fixed.</summary>
    Guid? RepairSourceAttemptId = null,
    /// <summary>The proved source attempt's number, else null.</summary>
    int? RepairSourceAttemptNumber = null);
