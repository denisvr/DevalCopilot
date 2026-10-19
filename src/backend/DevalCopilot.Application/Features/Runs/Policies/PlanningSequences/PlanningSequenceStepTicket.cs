namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// The internal identity under which the coordinator composes one of the three existing claim commands (ADR-0055). Its constructor is
/// internal to the Application and the claim commands expose it only through an internal initializer, so no MVC request, DTO or other
/// assembly can supply a sequence or step identity. The ticket carries identities only: every authority fact (the preparation token,
/// deadline, expected step and pinned source) is re-read from the database at the claim's durable seam.
/// </summary>
public sealed class PlanningSequenceStepTicket
{
    internal PlanningSequenceStepTicket(
        Guid sequenceId,
        Guid stepId,
        int ordinal,
        Guid reservedAttemptId,
        Guid ownerToken,
        Guid? predecessorAttemptId,
        Guid? inputProposalMessageId)
    {
        SequenceId = sequenceId;
        StepId = stepId;
        Ordinal = ordinal;
        ReservedAttemptId = reservedAttemptId;
        OwnerToken = ownerToken;
        PredecessorAttemptId = predecessorAttemptId;
        InputProposalMessageId = inputProposalMessageId;
    }

    public Guid SequenceId { get; }

    public Guid StepId { get; }

    public int Ordinal { get; }

    /// <summary>The attempt identity the claim must use, chosen once at reservation.</summary>
    public Guid ReservedAttemptId { get; }

    internal Guid OwnerToken { get; }

    /// <summary>The completed attempt whose result this step consumes; null only for the Planner step.</summary>
    internal Guid? PredecessorAttemptId { get; }

    /// <summary>The Proposal this step reviews or resolves; null only for the Planner step.</summary>
    internal Guid? InputProposalMessageId { get; }
}
