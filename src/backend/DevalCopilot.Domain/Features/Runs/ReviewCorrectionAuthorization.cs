namespace DevalCopilot.Domain.Features.Runs;

/// <summary>One durable, one-way human authorization for one additional correction claim.</summary>
public sealed class ReviewCorrectionAuthorization
{
    private ReviewCorrectionAuthorization()
    {
    }

    public static ReviewCorrectionAuthorization Create(
        Guid id,
        Guid runId,
        Guid escalationId,
        Guid humanInstructionMessageId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || escalationId == Guid.Empty || humanInstructionMessageId == Guid.Empty)
        {
            throw new ArgumentException("An authorization requires durable identifiers.");
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAtUtc));
        }

        return new ReviewCorrectionAuthorization
        {
            Id = id,
            RunId = runId,
            EscalationId = escalationId,
            HumanInstructionMessageId = humanInstructionMessageId,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid EscalationId { get; private set; }

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
