using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

/// <summary>The one transport mapping of a recorded collaboration entry, shared by the ADR-0035 page and the ADR-0036 reply parent so a
/// message is disclosed identically by both.</summary>
internal static class CollaborationHistoryEntryMapper
{
    private const string Unrecognized = "Unrecognized";
    private const string Available = "Available";
    private const string Unavailable = "Unavailable";

    public static CollaborationHistoryEntryResponse Map(CollaborationHistoryEntry entry) => new(
        entry.RunId,
        entry.Id,
        CollaborationHistoryCursor.Format(entry.Sequence),
        entry.OccurredAtUtc,
        entry.ProtocolVersion ?? Unrecognized,
        entry.Type ?? Unrecognized,
        entry.Provenance ?? Unrecognized,
        MapParticipant(entry.Actor),
        MapParticipant(entry.Recipient),
        entry.ContentAvailable ? Available : Unavailable,
        entry.Summary,
        entry.Fields.Select(field => new CollaborationHistoryFieldResponse(field.Name, field.Value)).ToArray());

    private static CollaborationHistoryParticipantResponse MapParticipant(CollaborationHistoryParticipant participant) => new(
        participant.Kind?.ToString() ?? Unrecognized, participant.Role?.ToString(), participant.Provider?.ToString());
}
