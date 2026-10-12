using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

internal static class CollaborationReplyProjection
{
    /// <summary>The reply-inspection projection: identity, sequence, version, type and reply reference only.</summary>
    public static IQueryable<CollaborationReplyReference> ProjectReference(this IQueryable<CollaborationMessage> messages)
    {
        const string version = CollaborationMessage.ProtocolVersionOne;

        return messages.Select(message => new CollaborationReplyReference(
            message.Sequence,
            message.Id,
            message.ProtocolVersion == version,
            message.Type == CollaborationMessageType.Proposal ? 1
                : message.Type == CollaborationMessageType.Acceptance ? 2
                : message.Type == CollaborationMessageType.Challenge ? 3
                : message.Type == CollaborationMessageType.Question ? 4
                : message.Type == CollaborationMessageType.Decision ? 5
                : message.Type == CollaborationMessageType.ExecutionReport ? 6
                : message.Type == CollaborationMessageType.ReviewFinding ? 7
                : message.Type == CollaborationMessageType.RevisionResponse ? 8
                : message.Type == CollaborationMessageType.Escalation ? 9
                : message.Type == CollaborationMessageType.ReviewApproval ? 10
                : message.Type == CollaborationMessageType.HumanInstruction ? 11
                : 0,
            message.InReplyToMessageId));
    }
}
