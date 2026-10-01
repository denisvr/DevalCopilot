namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One durable, one-way human authorization of exactly one initial implementation claim for the final
/// (depth-two) Proposal of a planning lineage, after its second challenge-resolution escalation (ADR-0016).
/// It binds the run, the escalation message, the final Proposal and the exact workspace, starting
/// checkpoint and fingerprint to one canonical HumanInstruction message, and it owns the only
/// authoritative nullable consumed-by-attempt link. It stores no rationale (the message does), claims no
/// Agent attempt, and is never renewed, revoked, or deleted by this model.
/// </summary>
public sealed class PlanningImplementationAuthorization
{
    private PlanningImplementationAuthorization()
    {
        FingerprintSha256 = string.Empty;
    }

    public static PlanningImplementationAuthorization Create(
        Guid id,
        Guid runId,
        Guid escalationMessageId,
        Guid finalProposalMessageId,
        Guid workspaceId,
        Guid checkpointId,
        string fingerprintSha256,
        Guid humanInstructionMessageId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty
            || runId == Guid.Empty
            || escalationMessageId == Guid.Empty
            || finalProposalMessageId == Guid.Empty
            || workspaceId == Guid.Empty
            || checkpointId == Guid.Empty
            || humanInstructionMessageId == Guid.Empty)
        {
            throw new ArgumentException("A planning implementation authorization requires durable identifiers.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);

        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAtUtc));
        }

        return new PlanningImplementationAuthorization
        {
            Id = id,
            RunId = runId,
            EscalationMessageId = escalationMessageId,
            FinalProposalMessageId = finalProposalMessageId,
            WorkspaceId = workspaceId,
            CheckpointId = checkpointId,
            FingerprintSha256 = fingerprintSha256,
            HumanInstructionMessageId = humanInstructionMessageId,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid EscalationMessageId { get; private set; }

    public Guid FinalProposalMessageId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid CheckpointId { get; private set; }

    public string FingerprintSha256 { get; private set; }

    public Guid HumanInstructionMessageId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? ConsumedByAttemptId { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public bool IsAvailable => !ConsumedByAttemptId.HasValue;

    public void Consume(Guid attemptId, DateTimeOffset nowUtc)
    {
        if (attemptId == Guid.Empty)
        {
            throw new ArgumentException("An attempt identity is required.", nameof(attemptId));
        }

        if (nowUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        if (nowUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc), "Consumption cannot precede authorization creation.");
        }

        if (!IsAvailable)
        {
            throw new InvalidOperationException("The authorization has already been consumed.");
        }

        ConsumedByAttemptId = attemptId;
        ConsumedAtUtc = nowUtc;
    }
}
