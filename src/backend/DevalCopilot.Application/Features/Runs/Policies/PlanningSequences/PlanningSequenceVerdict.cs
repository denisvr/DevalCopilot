namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// The result of judging a completed step from durable identity alone. <see cref="PredecessorAttemptId"/> and
/// <see cref="InputProposalMessageId"/> name the exact next step input of a <see cref="PlanningSequenceVerdictKind.Continue"/>;
/// <see cref="FinalProposalMessageId"/> and <see cref="ClosingMessageId"/> are the proved plan and its acceptance or escalation of an
/// ending verdict. Nothing here is selected from a latest message, a loaded page or a subset of challenges.
/// </summary>
internal sealed record PlanningSequenceVerdict(
    PlanningSequenceVerdictKind Kind,
    Guid? PredecessorAttemptId = null,
    Guid? InputProposalMessageId = null,
    Guid? FinalProposalMessageId = null,
    Guid? ClosingMessageId = null)
{
    public static PlanningSequenceVerdict Invalid() => new(PlanningSequenceVerdictKind.Invalid);
}
