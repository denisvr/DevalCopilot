namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One of at most five steps of a <see cref="PlanningSequence"/> (ADR-0055). Its unique (SequenceId, Ordinal), fixed role and exact
/// predecessor identity are decided before any external work; its <see cref="ReservedAttemptId"/> is chosen once at reservation, so a
/// claim that committed is recognized by that exact attempt row, never by a search for a latest attempt. The attempt's own ordered
/// inputs remain the single authoritative copy: this record keeps only the identity of the predecessor attempt and of the Proposal the
/// step reviews or resolves.
/// </summary>
public sealed class PlanningSequenceStep
{
    private PlanningSequenceStep()
    {
    }

    public static PlanningSequenceStep Reserve(
        Guid id,
        Guid sequenceId,
        int ordinal,
        Guid? predecessorAttemptId,
        Guid? inputProposalMessageId,
        Guid reservedAttemptId,
        Guid preparationOwnerToken,
        DateTimeOffset reservedAtUtc)
    {
        if (id == Guid.Empty || sequenceId == Guid.Empty || reservedAttemptId == Guid.Empty || preparationOwnerToken == Guid.Empty)
        {
            throw new ArgumentException("A planning sequence step requires durable identifiers.");
        }

        var role = PlanningSequencePolicy.RoleOf(ordinal);
        if (ordinal == 0 != (predecessorAttemptId is null) || ordinal == 0 != (inputProposalMessageId is null))
        {
            throw new ArgumentException("Only the Planner step has no predecessor; every later step names its exact predecessor input.");
        }

        if (reservedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedAtUtc));
        }

        return new PlanningSequenceStep
        {
            Id = id,
            SequenceId = sequenceId,
            Ordinal = ordinal,
            Role = role,
            State = PlanningSequenceStepState.Reserved,
            PredecessorAttemptId = predecessorAttemptId,
            InputProposalMessageId = inputProposalMessageId,
            ReservedAttemptId = reservedAttemptId,
            PreparationOwnerToken = preparationOwnerToken,
            PreparationReservedAtUtc = reservedAtUtc,
            PreparationDeadlineUtc = PlanningSequencePolicy.PreparationDeadlineOf(reservedAtUtc),
            Revision = 1,
            UpdatedAtUtc = reservedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid SequenceId { get; private set; }

    public int Ordinal { get; private set; }

    public AgentRole Role { get; private set; }

    public PlanningSequenceStepState State { get; private set; }

    /// <summary>The completed attempt whose result this step consumes; null only for the Planner step.</summary>
    public Guid? PredecessorAttemptId { get; private set; }

    /// <summary>The Proposal message this step reviews, or resolves the challenges of; null only for the Planner step.</summary>
    public Guid? InputProposalMessageId { get; private set; }

    /// <summary>The attempt identity chosen at reservation. A committed claim is exactly the attempt row with this id.</summary>
    public Guid ReservedAttemptId { get; private set; }

    /// <summary>Set once, in the same transaction as the attempt, and equal to <see cref="ReservedAttemptId"/>.</summary>
    public Guid? AttemptId { get; private set; }

    /// <summary>The token only the original live host worker holds; a duplicate tick or stale worker cannot present it.</summary>
    public Guid PreparationOwnerToken { get; private set; }

    public DateTimeOffset PreparationReservedAtUtc { get; private set; }

    public DateTimeOffset PreparationDeadlineUtc { get; private set; }

    public string? ReasonCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsOutstanding => State is PlanningSequenceStepState.Reserved or PlanningSequenceStepState.Claimed;

    /// <summary>Links the claimed attempt. Valid only for the exact reserved attempt identity.</summary>
    public void MarkClaimed(Guid attemptId, DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot be claimed.");
        }

        if (attemptId != ReservedAttemptId)
        {
            throw new ArgumentException("Only the reserved attempt identity can be linked.", nameof(attemptId));
        }

        AttemptId = attemptId;
        Move(PlanningSequenceStepState.Claimed, reasonCode: null, nowUtc);
    }

    /// <summary>The claimed attempt reached a terminal result and the coordinator classified it.</summary>
    public void Conclude(DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceStepState.Claimed)
        {
            throw new InvalidOperationException($"A {State} step cannot be concluded.");
        }

        Move(PlanningSequenceStepState.Concluded, reasonCode: null, nowUtc);
    }

    /// <summary>The preparation authority was revoked before any claim committed.</summary>
    public void Revoke(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot be revoked.");
        }

        Move(PlanningSequenceStepState.Revoked, reasonCode, nowUtc);
    }

    /// <summary>A failed preparation was neither proved persisted nor proved absent.</summary>
    public void MarkUnresolved(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot become unresolved.");
        }

        Move(PlanningSequenceStepState.Unresolved, reasonCode, nowUtc);
    }

    private void Move(PlanningSequenceStepState next, string? reasonCode, DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        if (reasonCode is not null && !PlanningSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed planning sequence reason.");
        }

        State = next;
        ReasonCode = reasonCode;
        Revision++;
        UpdatedAtUtc = nowUtc;
    }
}
