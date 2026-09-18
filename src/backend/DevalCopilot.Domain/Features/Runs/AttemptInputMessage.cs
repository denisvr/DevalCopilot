namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One ordered input <see cref="CollaborationMessage"/> a real Agent attempt was launched
/// against — the single authoritative record of which collaboration facts an attempt reviewed or
/// is resolving. A <see cref="AgentRole.CriticalReviewer"/> attempt has exactly one row (the
/// reviewed Proposal, at <see cref="Sequence"/> 0); a <see cref="AgentRole.Resolver"/> attempt has
/// the original Proposal at <see cref="Sequence"/> 0 followed by every Challenge in timeline
/// order at 1..N. Immutable once recorded — a later Challenge or a different Proposal is always a
/// new <see cref="Attempt"/>, never a mutation of this set. This is the only place an Agent
/// attempt's input identity is durably recorded; it is never duplicated onto a bare id column on
/// <see cref="Attempt"/> itself.
/// </summary>
public sealed class AttemptInputMessage
{
    private AttemptInputMessage()
    {
    }

    public static AttemptInputMessage Record(Guid id, Guid attemptId, Guid collaborationMessageId, int sequence)
    {
        if (id == Guid.Empty || attemptId == Guid.Empty || collaborationMessageId == Guid.Empty)
        {
            throw new ArgumentException("An attempt input message requires durable identifiers.");
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Must not be negative.");
        }

        return new AttemptInputMessage
        {
            Id = id,
            AttemptId = attemptId,
            CollaborationMessageId = collaborationMessageId,
            Sequence = sequence,
        };
    }

    public Guid Id { get; private set; }

    public Guid AttemptId { get; private set; }

    public Guid CollaborationMessageId { get; private set; }

    /// <summary>Zero-based, ordered position within this attempt's own input set. Always 0 for
    /// the attempt's root Proposal; for a <see cref="AgentRole.Resolver"/> attempt, 1..N are the
    /// Challenges being resolved, in the exact timeline order they were recorded.</summary>
    public int Sequence { get; private set; }
}
