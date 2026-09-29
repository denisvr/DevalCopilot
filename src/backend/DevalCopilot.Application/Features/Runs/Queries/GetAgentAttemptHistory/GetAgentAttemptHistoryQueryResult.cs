namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;

/// <summary>A page of history plus explicit continuation: <see cref="HasMore"/> is true exactly when
/// older Agent attempts remain, and <see cref="NextBeforeAttemptNumber"/> is then the cursor to pass
/// for the following page (null when there is none).</summary>
public sealed record GetAgentAttemptHistoryQueryResult(
    IReadOnlyList<AgentAttemptHistoryEntry> Items, bool HasMore, int? NextBeforeAttemptNumber);
