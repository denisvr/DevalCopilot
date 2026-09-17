using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

public static class CollaborationMessageReplyErrors
{
    public static Error Create(CollaborationMessageReplyViolation violation)
    {
        return violation switch
        {
            CollaborationMessageReplyViolation.ReplyRequired => Error.Conflict(
                "collaboration_messages.reply_required",
                "This collaboration message must reference an earlier message."),
            CollaborationMessageReplyViolation.ReplyNotAllowed => Error.Conflict(
                "collaboration_messages.reply_not_allowed",
                "A proposal cannot reference an earlier message."),
            CollaborationMessageReplyViolation.InvalidParentType => Error.Conflict(
                "collaboration_messages.invalid_reply_parent",
                "The referenced collaboration message is not a valid parent for this message type."),
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };
    }
}
