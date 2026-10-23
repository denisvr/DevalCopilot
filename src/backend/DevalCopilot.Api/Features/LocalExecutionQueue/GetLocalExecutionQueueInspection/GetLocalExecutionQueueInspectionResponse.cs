namespace DevalCopilot.Api.Features.LocalExecutionQueue.GetLocalExecutionQueueInspection;

/// <summary>
/// The recorded outstanding admissions of the whole database (ADR-0064). <c>Status</c> is <c>Empty</c> (no Waiting, Reserved or
/// Unresolved admission is recorded, which is neither an idle host nor free capacity), <c>Recorded</c> (one through 68 entries in
/// strictly increasing recorded admission order) or <c>Unavailable</c> (an unprovable reading); only <c>Recorded</c> carries entries, and
/// every other status carries an empty list, never a partial one.
/// </summary>
public sealed record GetLocalExecutionQueueInspectionResponse(
    string Status,
    IReadOnlyList<LocalExecutionQueueInspectionEntryResponse> Entries);
