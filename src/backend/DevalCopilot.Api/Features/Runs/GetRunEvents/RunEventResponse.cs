namespace DevalCopilot.Api.Features.Runs.GetRunEvents;

public sealed record RunEventResponse(
    long Sequence,
    Guid Id,
    Guid? AttemptId,
    string EventType,
    ParticipantIdentityResponse Actor,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc);
