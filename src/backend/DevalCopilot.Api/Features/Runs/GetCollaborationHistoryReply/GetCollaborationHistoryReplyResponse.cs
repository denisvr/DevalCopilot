using DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistoryReply;

/// <summary>The recorded reply relationship of one collaboration message. <c>SourceSequence</c> is the source's exact sequence as a
/// canonical decimal string. <c>Status</c> is <c>None</c> (a recognized version-1.0 Proposal with no reply reference),
/// <c>Unavailable</c> (the stored reference cannot be shown as a valid recorded relationship; no target identity or content) or
/// <c>Available</c>. <c>Parent</c> is non-null exactly when the status is <c>Available</c> and describes the recorded relationship
/// only: its own content status is independent and it is never a current approval or provider success.</summary>
public sealed record GetCollaborationHistoryReplyResponse(
    Guid RunId, Guid MessageId, string SourceSequence, string Status, CollaborationHistoryEntryResponse? Parent);
