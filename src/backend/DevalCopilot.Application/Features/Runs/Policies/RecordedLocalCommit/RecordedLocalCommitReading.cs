namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

internal sealed record RecordedLocalCommitReading(RecordedLocalCommitReadingKind Kind, RecordedLocalCommitOperation? Operation)
{
    public static readonly RecordedLocalCommitReading NotFound = new(RecordedLocalCommitReadingKind.NotFound, null);

    public static readonly RecordedLocalCommitReading NotRecorded = new(RecordedLocalCommitReadingKind.NotRecorded, null);

    public static readonly RecordedLocalCommitReading Unavailable = new(RecordedLocalCommitReadingKind.Unavailable, null);

    public static RecordedLocalCommitReading Recorded(RecordedLocalCommitOperation operation) =>
        new(RecordedLocalCommitReadingKind.Recorded, operation);
}
