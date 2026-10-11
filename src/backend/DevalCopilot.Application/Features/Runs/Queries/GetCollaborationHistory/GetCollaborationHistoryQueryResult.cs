namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>A page plus explicit continuation: <see cref="HasMore"/> is true exactly when older messages remain, and
/// <see cref="NextBeforeSequence"/> is then the last admitted sequence, the exclusive cursor of the following page. The page is
/// not an atomic snapshot of the run and carries no total.</summary>
public sealed record GetCollaborationHistoryQueryResult(
    Guid RunId, IReadOnlyList<CollaborationHistoryEntry> Entries, bool HasMore, long? NextBeforeSequence);
