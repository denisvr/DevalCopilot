namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputMessage;

/// <summary>The recorded content of one selected input message of an Agent attempt (ADR-0053): a stored collaboration input as read now,
/// never the complete prompt, all context, proof of what the Agent saw, a current approval or provider success. <c>Status</c> is
/// <c>Unavailable</c> (no attempt, input or message: the identity, the size or the coherence of the whole input set, or the reread
/// message header, could not be proved, and which is not disclosed) or <c>Recorded</c> (the attempt, the selected input and that
/// message, whose content may itself be unavailable).</summary>
public sealed record GetAgentAttemptInputMessageResponse(
    Guid RunId,
    Guid AttemptId,
    Guid CollaborationMessageId,
    string Status,
    AgentAttemptInputMessageAttemptResponse? Attempt,
    AgentAttemptInputMessageInputResponse? Input,
    AgentAttemptInputCollaborationMessageResponse? Message);
