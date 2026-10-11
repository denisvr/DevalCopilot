namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>One recorded collaboration message. <see cref="ProtocolVersion"/>, <see cref="Type"/> and <see cref="Provenance"/> are
/// null when the stored value is not one this version recognizes; the row is kept and the stored text is never exposed. When
/// <see cref="ContentAvailable"/> is false the summary is null and there are no fields: nothing malformed, unsafe or oversized is
/// ever truncated, repaired or returned.</summary>
public sealed record CollaborationHistoryEntry(
    Guid RunId,
    Guid Id,
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    string? ProtocolVersion,
    string? Type,
    string? Provenance,
    CollaborationHistoryParticipant Actor,
    CollaborationHistoryParticipant Recipient,
    bool ContentAvailable,
    string? Summary,
    IReadOnlyList<CollaborationHistoryField> Fields);
