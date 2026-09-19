namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One ordered verification-execution membership a real <see cref="AgentRole.CodeReviewer"/>
/// attempt claimed as evidence at claim time — before the provider is ever invoked. Mirrors
/// <see cref="AttemptInputMessage"/> exactly, one level further down: where
/// <see cref="AttemptInputMessage"/> durably records which <see cref="CollaborationMessage"/> set
/// an attempt was launched against, this entity durably records the exact ordered set of currently
/// enabled verification-command executions — bound to the exact same result checkpoint the review
/// attempt claims — that the review evaluated. Never a JSON column on <see cref="Attempt"/>: a
/// relational membership row per claimed execution, with its own ownership and uniqueness
/// constraints, is the only authoritative record of this set. Immutable once recorded.
/// </summary>
public sealed class AttemptVerificationEvidence
{
    private AttemptVerificationEvidence()
    {
    }

    public static AttemptVerificationEvidence Record(
        Guid id, Guid attemptId, Guid verificationCommandId, Guid verificationExecutionId, int sequence)
    {
        if (id == Guid.Empty || attemptId == Guid.Empty || verificationCommandId == Guid.Empty || verificationExecutionId == Guid.Empty)
        {
            throw new ArgumentException("An attempt verification evidence membership requires durable identifiers.");
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Must not be negative.");
        }

        return new AttemptVerificationEvidence
        {
            Id = id,
            AttemptId = attemptId,
            VerificationCommandId = verificationCommandId,
            VerificationExecutionId = verificationExecutionId,
            Sequence = sequence,
        };
    }

    public Guid Id { get; private set; }

    public Guid AttemptId { get; private set; }

    public Guid VerificationCommandId { get; private set; }

    public Guid VerificationExecutionId { get; private set; }

    /// <summary>Zero-based, ordered position within this attempt's own claimed verification set —
    /// deterministic (by the owning <c>VerificationCommand.CommandNumber</c> at claim time), never
    /// an arbitrary selection when several enabled commands exist.</summary>
    public int Sequence { get; private set; }
}
