namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>
/// <see cref="Unavailable"/>: no header, no count, no references, not omitted. <see cref="Empty"/>: the header, count 0, no references,
/// not omitted. <see cref="Recorded"/>: the header, the exact inspected count from 1 to 64, the first at most 10 references in stored order
/// and <see cref="InputMessagesOmitted"/> exactly when the count exceeds them. The prefix is not a page and claims no complete history.
/// </summary>
public sealed record GetAgentAttemptInputsQueryResult(
    Guid RunId,
    Guid AttemptId,
    AgentAttemptInputsStatus Status,
    AgentAttemptInputsHeader? Attempt,
    IReadOnlyList<AgentAttemptInputReference> InputMessages,
    int? InputMessageTotalCount,
    bool InputMessagesOmitted)
{
    public static GetAgentAttemptInputsQueryResult Unavailable(Guid runId, Guid attemptId) =>
        new(runId, attemptId, AgentAttemptInputsStatus.Unavailable, null, [], null, false);
}
