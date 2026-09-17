namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// Version-1.0 reply semantics for project-owned collaboration facts. Database ownership
/// and parent lookup remain Application responsibilities; this policy owns only protocol meaning.
/// </summary>
public static class CollaborationMessageReplyPolicy
{
    public static CollaborationMessageReplyViolation EvaluateReference(
        CollaborationMessageType type,
        bool hasReply)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (type == CollaborationMessageType.Proposal)
        {
            return hasReply
                ? CollaborationMessageReplyViolation.ReplyNotAllowed
                : CollaborationMessageReplyViolation.None;
        }

        return hasReply
            ? CollaborationMessageReplyViolation.None
            : CollaborationMessageReplyViolation.ReplyRequired;
    }

    public static CollaborationMessageReplyViolation Evaluate(
        CollaborationMessageType type,
        CollaborationMessageType? parentType)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        var referenceViolation = EvaluateReference(type, parentType.HasValue);
        if (referenceViolation != CollaborationMessageReplyViolation.None)
        {
            return referenceViolation;
        }

        if (!parentType.HasValue)
        {
            return CollaborationMessageReplyViolation.None;
        }

        return IsAllowedParent(type, parentType.Value)
            ? CollaborationMessageReplyViolation.None
            : CollaborationMessageReplyViolation.InvalidParentType;
    }

    private static bool IsAllowedParent(CollaborationMessageType type, CollaborationMessageType parentType)
    {
        return type switch
        {
            CollaborationMessageType.Acceptance or CollaborationMessageType.Challenge =>
                parentType == CollaborationMessageType.Proposal,
            CollaborationMessageType.Decision =>
                parentType is CollaborationMessageType.Proposal or CollaborationMessageType.Challenge,
            CollaborationMessageType.ExecutionReport => parentType == CollaborationMessageType.Decision,
            CollaborationMessageType.ReviewFinding => parentType == CollaborationMessageType.ExecutionReport,
            CollaborationMessageType.RevisionResponse => parentType == CollaborationMessageType.ReviewFinding,
            // A question is scoped to a concrete piece of active collaboration rather than
            // being an unbounded side conversation.
            CollaborationMessageType.Question => parentType is CollaborationMessageType.Proposal
                or CollaborationMessageType.Challenge
                or CollaborationMessageType.Decision
                or CollaborationMessageType.ExecutionReport
                or CollaborationMessageType.ReviewFinding,
            // An escalation records why an already-existing collaboration fact cannot be
            // resolved safely; it never creates an ungrounded escalation thread.
            CollaborationMessageType.Escalation => parentType is CollaborationMessageType.Proposal
                or CollaborationMessageType.Challenge
                or CollaborationMessageType.Decision
                or CollaborationMessageType.ExecutionReport
                or CollaborationMessageType.ReviewFinding
                or CollaborationMessageType.RevisionResponse
                or CollaborationMessageType.Question,
            _ => false,
        };
    }
}
