namespace DevalCopilot.Domain.Features.Runs;

/// <summary>Durable human-attention fact for an exhausted review-correction budget.</summary>
public sealed class ReviewCorrectionEscalation
{
    private ReviewCorrectionEscalation()
    {
    }

    public static ReviewCorrectionEscalation Record(
        Guid id,
        Guid runId,
        Guid implementationReviewAttemptId,
        Guid collaborationMessageId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || implementationReviewAttemptId == Guid.Empty || collaborationMessageId == Guid.Empty)
        {
            throw new ArgumentException("An escalation requires durable identifiers.");
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAtUtc));
        }

        return new ReviewCorrectionEscalation
        {
            Id = id,
            RunId = runId,
            ImplementationReviewAttemptId = implementationReviewAttemptId,
            CollaborationMessageId = collaborationMessageId,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ImplementationReviewAttemptId { get; private set; }

    public Guid CollaborationMessageId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
