namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

/// <param name="Sequence">The exact database-assigned sequence as a canonical decimal string (it can exceed a browser number). Gaps
/// between one run's messages are valid; it is the only ordering authority.</param>
/// <param name="ProtocolVersion">Exactly <c>1.0</c>, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="Type">One of the known message type names, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="Provenance">One of the known provenance names, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="ContentStatus"><c>Available</c> only when the metadata is recognized and coherent and the summary and fields passed
/// the existing content policy; otherwise <c>Unavailable</c>, with a null summary and no fields.</param>
public sealed record CollaborationHistoryEntryResponse(
    Guid RunId,
    Guid Id,
    string Sequence,
    DateTimeOffset OccurredAtUtc,
    string ProtocolVersion,
    string Type,
    string Provenance,
    CollaborationHistoryParticipantResponse Actor,
    CollaborationHistoryParticipantResponse Recipient,
    string ContentStatus,
    string? Summary,
    IReadOnlyList<CollaborationHistoryFieldResponse> Fields);
