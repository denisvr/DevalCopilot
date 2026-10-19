namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputMessage;

/// <summary>The selected input's collaboration message as the ADR-0035/0036 admission discloses it. Its identity, type, version,
/// sequence and time equal the selected input.</summary>
/// <param name="Sequence">The exact database-assigned sequence as a canonical decimal string.</param>
/// <param name="OccurredAtUtc">The recorded time as an invariant UTC round-trip string (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>).</param>
/// <param name="ProtocolVersion">Exactly <c>1.0</c>.</param>
/// <param name="Type">The recognized message type name.</param>
/// <param name="Provenance">One of the known provenance names, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="ContentStatus"><c>Available</c> only when the metadata is recognized and coherent and the summary and fields passed
/// the existing content policy; otherwise <c>Unavailable</c>, with a null summary and no fields.</param>
public sealed record AgentAttemptInputCollaborationMessageResponse(
    Guid RunId,
    Guid Id,
    string Sequence,
    string OccurredAtUtc,
    string ProtocolVersion,
    string Type,
    string Provenance,
    AgentAttemptInputMessageParticipantResponse Actor,
    AgentAttemptInputMessageParticipantResponse Recipient,
    string ContentStatus,
    string? Summary,
    IReadOnlyList<AgentAttemptInputMessageFieldResponse> Fields);
