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

    /// <summary>Records a membership of a verification-diagnosis attempt (ADR-0018). In addition to the ordered identifiers it
    /// carries the versioned SHA-256 of the canonical snapshot of the command, execution, and failed-output facts the claim
    /// decided against, so a change to any of them while the identifiers stay the same is detected. The relational rows remain
    /// the only membership authority; the digest is a per-row integrity fact, never a second membership store.</summary>
    public static AttemptVerificationEvidence RecordDiagnosisSnapshot(
        Guid id, Guid attemptId, Guid verificationCommandId, Guid verificationExecutionId, int sequence, string snapshotSha256)
    {
        if (!IsValidSnapshotDigest(snapshotSha256))
        {
            throw new ArgumentException("A diagnosis verification snapshot requires a lowercase 64-character hexadecimal SHA-256.", nameof(snapshotSha256));
        }

        var evidence = Record(id, attemptId, verificationCommandId, verificationExecutionId, sequence);
        evidence.SnapshotSha256 = snapshotSha256;
        return evidence;
    }

    public const int SnapshotDigestLength = 64;

    public static bool IsValidSnapshotDigest(string? value) =>
        value is { Length: SnapshotDigestLength } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public Guid Id { get; private set; }

    public Guid AttemptId { get; private set; }

    public Guid VerificationCommandId { get; private set; }

    public Guid VerificationExecutionId { get; private set; }

    /// <summary>Zero-based, ordered position within this attempt's own claimed verification set —
    /// deterministic (by the owning <c>VerificationCommand.CommandNumber</c> at claim time), never
    /// an arbitrary selection when several enabled commands exist.</summary>
    public int Sequence { get; private set; }

    /// <summary>Null for an ordinary (historical) review membership; required and valid for a verification-diagnosis membership.</summary>
    public string? SnapshotSha256 { get; private set; }
}
