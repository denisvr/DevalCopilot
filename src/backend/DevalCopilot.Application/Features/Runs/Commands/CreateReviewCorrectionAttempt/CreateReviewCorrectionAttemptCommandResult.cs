namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

public abstract record CreateReviewCorrectionAttemptCommandResult
{
    public sealed record AttemptCreated : CreateReviewCorrectionAttemptCommandResult
    {
        public AttemptCreated(Guid attemptId, int attemptNumber, long? latestEventSequence = null)
        {
            if (attemptId == Guid.Empty)
            {
                throw new ArgumentException("An attempt identity is required.", nameof(attemptId));
            }

            if (attemptNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attemptNumber));
            }

            AttemptId = attemptId;
            AttemptNumber = attemptNumber;
            LatestEventSequence = latestEventSequence;
        }

        public Guid AttemptId { get; }

        public int AttemptNumber { get; }

        public long? LatestEventSequence { get; }
    }

    public sealed record Escalated : CreateReviewCorrectionAttemptCommandResult
    {
        public Escalated(Guid escalationId, Guid escalationMessageId, long? latestEventSequence = null)
        {
            if (escalationId == Guid.Empty)
            {
                throw new ArgumentException("An escalation identity is required.", nameof(escalationId));
            }

            if (escalationMessageId == Guid.Empty)
            {
                throw new ArgumentException("An escalation message identity is required.", nameof(escalationMessageId));
            }

            EscalationId = escalationId;
            EscalationMessageId = escalationMessageId;
            LatestEventSequence = latestEventSequence;
        }

        public Guid EscalationId { get; }

        public Guid EscalationMessageId { get; }

        public long? LatestEventSequence { get; }
    }
}
