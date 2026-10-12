using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

/// <summary><see cref="Parent"/> is non-null exactly when <see cref="Status"/> is <see cref="CollaborationReplyStatus.Available"/>.
/// Available describes the recorded protocol relationship only: the parent entry keeps the ADR-0035 disclosure, so its own content may
/// still be unavailable.</summary>
public sealed record GetCollaborationHistoryReplyQueryResult(
    Guid RunId, Guid MessageId, long SourceSequence, CollaborationReplyStatus Status, CollaborationHistoryEntry? Parent);
