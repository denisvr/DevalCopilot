namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

/// <summary>A page plus explicit continuation: <see cref="HasMore"/> is true exactly when older runs remain, and
/// <see cref="NextBeforeExecutionNumber"/> is then the last admitted execution number, the cursor of the following page.</summary>
public sealed record GetProjectRunHistoryQueryResult(
    Guid ProjectId, IReadOnlyList<ProjectRunHistoryEntry> Entries, bool HasMore, int? NextBeforeExecutionNumber);
