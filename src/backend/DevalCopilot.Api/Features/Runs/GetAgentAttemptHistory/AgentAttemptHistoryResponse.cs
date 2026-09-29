namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptHistory;

/// <summary>
/// One page of a run's Agent attempts, newest first. <c>HasMore</c> is true exactly when older
/// Agent attempts remain; pass <c>NextBeforeAttemptNumber</c> as <c>beforeAttemptNumber</c> for the
/// next page. Bounded to identity and lifecycle facts — never a storage path, content hash,
/// provider session identifier, prompt, or artifact content.
/// </summary>
public sealed record AgentAttemptHistoryResponse(
    IReadOnlyList<AgentAttemptHistoryEntryResponse> Items, bool HasMore, int? NextBeforeAttemptNumber);
