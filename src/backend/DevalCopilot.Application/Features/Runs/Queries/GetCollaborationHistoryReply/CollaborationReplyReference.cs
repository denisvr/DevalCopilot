namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

/// <summary>The header and reply reference of one message, classified by the database like the ADR-0035 row but
/// without any text column: what the reply inspection needs to decide the recorded relationship of its source.</summary>
internal sealed record CollaborationReplyReference(long Sequence, Guid Id, bool VersionKnown, int TypeCode, Guid? InReplyToMessageId);
