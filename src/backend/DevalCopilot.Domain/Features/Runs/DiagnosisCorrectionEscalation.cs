namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// Durable human-attention fact for an exhausted shared review-correction budget at a verification diagnosis's findings
/// (ADR-0018). It is uniquely bound to the diagnosis attempt, never stored under an ordinary implementation-review
/// identity, and it carries no authorization: no extra-correction grant exists for this source, and the ordinary review
/// escalation's grants cannot be transferred to it.
/// </summary>
public sealed class DiagnosisCorrectionEscalation
{
    private DiagnosisCorrectionEscalation()
    {
    }

    public static DiagnosisCorrectionEscalation Record(
        Guid id,
        Guid runId,
        Guid verificationDiagnosisAttemptId,
        Guid collaborationMessageId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || verificationDiagnosisAttemptId == Guid.Empty || collaborationMessageId == Guid.Empty)
        {
            throw new ArgumentException("A diagnosis correction escalation requires durable identifiers.");
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAtUtc));
        }

        return new DiagnosisCorrectionEscalation
        {
            Id = id,
            RunId = runId,
            VerificationDiagnosisAttemptId = verificationDiagnosisAttemptId,
            CollaborationMessageId = collaborationMessageId,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid VerificationDiagnosisAttemptId { get; private set; }

    public Guid CollaborationMessageId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
