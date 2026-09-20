using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationTimeline;

public sealed record CollaborationMessageTimelineQueryResult(
    long Sequence,
    Guid Id,
    Guid? AttemptId,
    string ProtocolVersion,
    ParticipantIdentity Actor,
    ParticipantIdentity Recipient,
    CollaborationMessageType Type,
    Guid? InReplyToMessageId,
    string Summary,
    string StructuredContentJson,
    CollaborationMessageProvenance Provenance,
    DateTimeOffset OccurredAtUtc);
