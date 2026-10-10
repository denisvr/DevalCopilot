namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>A page plus explicit continuation: <see cref="HasMore"/> is true exactly when older headers remain, and
/// <see cref="NextBeforeSequence"/> is then the last admitted sequence, the exclusive cursor of the following page. The page is
/// not an atomic snapshot of the run and carries no total.</summary>
public sealed record GetRunEventHistoryQueryResult(
    Guid RunId, IReadOnlyList<RunEventHistoryEntry> Entries, bool HasMore, long? NextBeforeSequence);
