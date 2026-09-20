using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEvents;

public sealed record RunEventQueryResult(
    long Sequence,
    Guid Id,
    Guid? AttemptId,
    string EventType,
    ParticipantIdentity Actor,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc);
