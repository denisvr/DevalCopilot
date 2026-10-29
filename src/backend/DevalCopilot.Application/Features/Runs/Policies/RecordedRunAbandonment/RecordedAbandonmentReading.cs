namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

internal sealed record RecordedAbandonmentReading(RecordedAbandonmentReadingKind Kind, RecordedAbandonment? Abandonment)
{
    public static readonly RecordedAbandonmentReading NotFound = new(RecordedAbandonmentReadingKind.NotFound, null);

    public static readonly RecordedAbandonmentReading NotRecorded = new(RecordedAbandonmentReadingKind.NotRecorded, null);

    public static readonly RecordedAbandonmentReading Unavailable = new(RecordedAbandonmentReadingKind.Unavailable, null);

    public static RecordedAbandonmentReading Recorded(RecordedAbandonment abandonment) =>
        new(RecordedAbandonmentReadingKind.Recorded, abandonment);
}
