namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;

/// <summary>
/// <see cref="Unavailable"/>: no header, no count and no references. <see cref="Empty"/>: the header, count 0 and no references.
/// <see cref="Recorded"/>: the header, the exact count from 1 to 64 and every reference in sequence order, without omission. The set is
/// stored associations as read now, not the complete provider response and not an atomic snapshot.
/// </summary>
public sealed record GetAgentAttemptOutputsQueryResult(
    Guid RunId,
    Guid AttemptId,
    AgentAttemptOutputsStatus Status,
    AgentAttemptOutputsHeader? Attempt,
    int? OutputMessageTotalCount,
    IReadOnlyList<AgentAttemptOutputReference> OutputMessages)
{
    public static GetAgentAttemptOutputsQueryResult Unavailable(Guid runId, Guid attemptId) =>
        new(runId, attemptId, AgentAttemptOutputsStatus.Unavailable, null, null, []);
}
