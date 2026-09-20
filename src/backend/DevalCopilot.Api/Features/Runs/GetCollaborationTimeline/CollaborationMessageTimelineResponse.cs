namespace DevalCopilot.Api.Features.Runs.GetCollaborationTimeline;

/// <summary>
/// A bounded project-owned protocol envelope. It deliberately excludes provider transcripts,
/// executable paths, raw output, environment values, credentials, and artifact locations.
/// </summary>
public sealed record CollaborationMessageTimelineResponse(
    long Sequence,
    Guid Id,
    Guid? AttemptId,
    string ProtocolVersion,
    ParticipantIdentityResponse Actor,
    ParticipantIdentityResponse Recipient,
    string Type,
    Guid? InReplyToMessageId,
    string Summary,
    string StructuredContentJson,
    string Provenance,
    DateTimeOffset OccurredAtUtc);
