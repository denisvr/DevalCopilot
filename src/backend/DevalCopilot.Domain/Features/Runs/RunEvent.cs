namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable, sequenced fact for a run's timeline and replay. <see cref="Sequence"/>
/// is a monotonic database-assigned ordinal; <see cref="Id"/> is the durable identifier.
/// </summary>
public sealed class RunEvent
{
    private RunEvent()
    {
    }

    public static RunEvent Record(
        Guid id,
        Guid runId,
        Guid? attemptId,
        string eventType,
        ParticipantKind actor,
        string payloadJson,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        return new RunEvent
        {
            Id = id,
            RunId = runId,
            AttemptId = attemptId,
            EventType = eventType,
            Actor = actor,
            PayloadJson = payloadJson,
            OccurredAtUtc = occurredAtUtc,
        };
    }

    /// <summary>
    /// Monotonic database sequence. Assigned on insert; ordering is defined by this
    /// value, never by <see cref="OccurredAtUtc"/>.
    /// </summary>
    public long Sequence { get; private set; }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid? AttemptId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public ParticipantKind Actor { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
