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
            // A reply is optional, not forbidden: a root Proposal starts a thread with no reply,
            // while a revised Proposal replies to the prior Proposal it supersedes. Either shape
            // is valid at this reference-only level; IsAllowedParent below still requires that a
            // Proposal's parent, when one exists, is itself a Proposal.
            return CollaborationMessageReplyViolation.None;
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
            // A revised Proposal's only valid parent is the prior Proposal it supersedes —
            // Application-layer validation additionally requires that parent to be an older
            // Proposal from the same run, never a cross-run, self-referential, or
            // forward-referencing one; this policy only owns the type-level shape.
            CollaborationMessageType.Proposal => parentType == CollaborationMessageType.Proposal,
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
