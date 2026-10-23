namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>Only <see cref="LocalExecutionQueueInspectionStatus.Recorded"/> carries entries (one through 68, in recorded admission
/// order); Empty and Unavailable carry none, never a partial list or count.</summary>
public sealed record GetLocalExecutionQueueInspectionQueryResult(
    LocalExecutionQueueInspectionStatus Status,
    IReadOnlyList<LocalExecutionQueueInspectionEntry> Entries);
