namespace DevalCopilot.Domain.Features.Runs;

public enum CollaborationMessageReplyViolation
{
    None = 0,
    ReplyRequired = 1,
    ReplyNotAllowed = 2,
    InvalidParentType = 3,
}
