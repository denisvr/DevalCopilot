namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputs;

/// <summary>The recorded collaboration-input references of one Agent attempt (ADR-0051): recorded inputs only, never the complete prompt,
/// context or a resumable session. <c>Status</c> is <c>Unavailable</c> (no attempt, no count, no references, not omitted; the identity,
/// the size of the set or its coherence could not be proved, and which is not disclosed), <c>Empty</c> (a Planner attempt that recorded no
/// input: its attempt, count 0, no references, not omitted) or <c>Recorded</c> (its attempt, the exact count from 1 to 64, the first at most
/// 10 references in stored order and <c>InputMessagesOmitted</c> exactly when the count exceeds them).</summary>
public sealed record GetAgentAttemptInputsResponse(
    Guid RunId,
    Guid AttemptId,
    string Status,
    AgentAttemptInputsHeaderResponse? Attempt,
    IReadOnlyList<AgentAttemptInputReferenceResponse> InputMessages,
    int? InputMessageTotalCount,
    bool InputMessagesOmitted);
