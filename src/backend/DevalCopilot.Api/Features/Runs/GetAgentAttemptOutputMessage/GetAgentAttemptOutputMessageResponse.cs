namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputMessage;

/// <summary>The recorded content of one selected output message of an Agent attempt (ADR-0054): a stored message attributed to the
/// attempt as read now, never its complete provider response, proof of what was generated or seen, a current approval or execution
/// success. <c>Status</c> is <c>Unavailable</c> (no attempt, output or message: the identity, the size or the coherence of the whole
/// output set, or the reread association, header, provenance or actor, could not be proved, and which is not disclosed) or
/// <c>Recorded</c> (the attempt, the selected output and that message, whose content may itself be unavailable).</summary>
public sealed record GetAgentAttemptOutputMessageResponse(
    Guid RunId,
    Guid AttemptId,
    Guid CollaborationMessageId,
    string Status,
    AgentAttemptOutputMessageAttemptResponse? Attempt,
    AgentAttemptOutputMessageOutputResponse? Output,
    AgentAttemptOutputCollaborationMessageResponse? Message);
