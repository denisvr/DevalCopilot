namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>One recorded event header. <see cref="EventType"/> is exactly one of the existing <c>RunEventType</c> constants, or null
/// when the stored value is not one of them; the row is kept and the stored text is never exposed. Nothing of the event's payload,
/// attempt, messages or artifacts is read.</summary>
public sealed record RunEventHistoryEntry(
    Guid RunId, Guid Id, long Sequence, string? EventType, RunEventHistoryActor Actor, DateTimeOffset OccurredAtUtc);
