namespace DevalCopilot.Api.Features.Runs.GetRunEventHistory;

/// <param name="Sequence">The exact database-assigned sequence as a canonical decimal string (it can exceed a browser number).
/// Gaps between one run's events are valid; it is the only ordering authority.</param>
/// <param name="EventType">One of the known event type names, or the fixed value <c>Unrecognized</c>; a stored value is never
/// exposed.</param>
public sealed record RunEventHistoryEntryResponse(
    Guid RunId, Guid Id, string Sequence, DateTimeOffset OccurredAtUtc, string EventType, RunEventHistoryActorResponse Actor);
