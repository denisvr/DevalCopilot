using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

/// <summary>
/// <see cref="Unavailable"/>: no header, no output and no message. <see cref="Recorded"/>: the header, the selected output and that
/// message as the ADR-0035/0036 admission discloses it, whose identity, type, version, sequence and time equal the output. Its content
/// may be unavailable (null summary, no fields) without invalidating the reference. Separate reads claim no atomic snapshot.
/// </summary>
public sealed record GetAgentAttemptOutputMessageQueryResult(
    Guid RunId,
    Guid AttemptId,
    Guid CollaborationMessageId,
    AgentAttemptOutputMessageStatus Status,
    AgentAttemptOutputMessageHeader? Attempt,
    AgentAttemptOutputMessageReference? Output,
    CollaborationHistoryEntry? Message)
{
    public static GetAgentAttemptOutputMessageQueryResult Unavailable(Guid runId, Guid attemptId, Guid messageId) =>
        new(runId, attemptId, messageId, AgentAttemptOutputMessageStatus.Unavailable, null, null, null);
}
