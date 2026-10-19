namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputs;

/// <summary>The stored collaboration messages associated with one Agent attempt (ADR-0054), as read now: never the complete provider
/// response, proof of what was generated or seen, a current approval or execution success. <c>Status</c> is <c>Unavailable</c> (no
/// attempt, no count and no references; the identity, the size of the set or its coherence could not be proved, and which is not
/// disclosed), <c>Empty</c> (the attempt and count 0; no associated message was recorded in this read) or <c>Recorded</c> (the attempt,
/// the exact count from 1 to 64 and every reference in sequence order, without omission).</summary>
public sealed record GetAgentAttemptOutputsResponse(
    Guid RunId,
    Guid AttemptId,
    string Status,
    AgentAttemptOutputsHeaderResponse? Attempt,
    int? OutputMessageTotalCount,
    IReadOnlyList<AgentAttemptOutputReferenceResponse> OutputMessages);
