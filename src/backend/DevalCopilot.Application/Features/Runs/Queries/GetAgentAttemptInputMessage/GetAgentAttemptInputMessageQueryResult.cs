using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;

/// <summary>
/// <see cref="Unavailable"/>: no header, no input and no message. <see cref="Recorded"/>: the header, the selected input and that
/// message as the ADR-0035/0036 admission discloses it, whose identity, type, version, sequence and time equal the input. Its content
/// may be unavailable (null summary, no fields) without invalidating the reference. Separate reads claim no atomic snapshot.
/// </summary>
public sealed record GetAgentAttemptInputMessageQueryResult(
    Guid RunId,
    Guid AttemptId,
    Guid CollaborationMessageId,
    AgentAttemptInputMessageStatus Status,
    AgentAttemptInputMessageHeader? Attempt,
    AgentAttemptInputMessageReference? Input,
    CollaborationHistoryEntry? Message)
{
    public static GetAgentAttemptInputMessageQueryResult Unavailable(Guid runId, Guid attemptId, Guid messageId) =>
        new(runId, attemptId, messageId, AgentAttemptInputMessageStatus.Unavailable, null, null, null);
}
