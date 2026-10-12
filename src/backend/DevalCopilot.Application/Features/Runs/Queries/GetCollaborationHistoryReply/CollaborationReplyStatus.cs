namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

/// <summary>The recorded protocol relationship of one message to its reply parent. <see cref="None"/> is only a recognized version-1.0
/// Proposal with no reply reference. <see cref="Unavailable"/> covers every stored shape that cannot be shown as a valid recorded
/// relationship; it never says which, and carries no target identity or content.</summary>
public enum CollaborationReplyStatus
{
    None,
    Unavailable,
    Available,
}
