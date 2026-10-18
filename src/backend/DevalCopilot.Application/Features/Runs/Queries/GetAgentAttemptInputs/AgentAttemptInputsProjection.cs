using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>The bounded, untracked scalar projections of the input-set read. Nothing outside the listed columns is ever selected: no
/// message summary or content JSON and no attempt data beyond the shared identity projection.</summary>
internal static class AgentAttemptInputsProjection
{
    /// <summary>The number of distinct positions a coherent inspected set can hold (0 through 63).</summary>
    public const int PositionBound = 64;

    public static readonly CollaborationMessageType[] TypesByCode =
    [
        CollaborationMessageType.Proposal,
        CollaborationMessageType.Acceptance,
        CollaborationMessageType.Challenge,
        CollaborationMessageType.Question,
        CollaborationMessageType.Decision,
        CollaborationMessageType.ExecutionReport,
        CollaborationMessageType.ReviewFinding,
        CollaborationMessageType.RevisionResponse,
        CollaborationMessageType.Escalation,
        CollaborationMessageType.ReviewApproval,
        CollaborationMessageType.HumanInstruction,
    ];

    // The position column has INTEGER affinity but SQLite keeps TEXT, REAL or BLOB that it cannot convert, and reading those as an
    // integer would silently yield 0 or a truncated value. Numbers sort below TEXT and BLOB, so the range admits only numbers, and `%`
    // casts to an integer, so only a stored INTEGER (never a REAL such as 7.5) equals its own remainder. Anything else reads as null.
    public static IQueryable<AgentAttemptInputRow> ProjectInput(this IQueryable<AttemptInputMessage> inputs) =>
        inputs.Select(input => new AgentAttemptInputRow(
            input.Sequence >= 0 && input.Sequence < PositionBound && input.Sequence % PositionBound == input.Sequence
                ? input.Sequence
                : null,
            input.CollaborationMessageId));

    // The message sequence is the table's INTEGER PRIMARY KEY (the rowid), which SQLite stores only as an exact 64-bit integer.
    public static IQueryable<AgentAttemptReferencedMessageRow> ProjectReferencedMessage(this IQueryable<CollaborationMessage> messages)
    {
        const string version = CollaborationMessage.ProtocolVersionOne;

        return messages.Select(message => new AgentAttemptReferencedMessageRow(
            message.Id,
            message.Sequence,
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
            message.OccurredAtUtc));
    }
}
