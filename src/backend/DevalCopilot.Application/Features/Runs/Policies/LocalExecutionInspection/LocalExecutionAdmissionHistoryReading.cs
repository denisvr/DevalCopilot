namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The outcome of reading one run's recorded admission history. Only <see cref="LocalExecutionAdmissionHistoryReadingKind.Recorded"/>
/// carries the proved header and ledger; every other kind carries neither, never a partial list.</summary>
internal sealed record LocalExecutionAdmissionHistoryReading(
    LocalExecutionAdmissionHistoryReadingKind Kind,
    LocalExecutionAdmissionHistoryHeader? Header,
    IReadOnlyList<LocalExecutionInspectionAdmission> Admissions)
{
    public static readonly LocalExecutionAdmissionHistoryReading RunNotFound = Without(LocalExecutionAdmissionHistoryReadingKind.RunNotFound);

    public static readonly LocalExecutionAdmissionHistoryReading NotRecorded = Without(LocalExecutionAdmissionHistoryReadingKind.NotRecorded);

    public static readonly LocalExecutionAdmissionHistoryReading NotQueued = Without(LocalExecutionAdmissionHistoryReadingKind.NotQueued);

    public static readonly LocalExecutionAdmissionHistoryReading Unavailable = Without(LocalExecutionAdmissionHistoryReadingKind.Unavailable);

    public static LocalExecutionAdmissionHistoryReading Recorded(
        LocalExecutionAdmissionHistoryHeader header, IReadOnlyList<LocalExecutionInspectionAdmission> admissions) =>
        new(LocalExecutionAdmissionHistoryReadingKind.Recorded, header, admissions);

    private static LocalExecutionAdmissionHistoryReading Without(LocalExecutionAdmissionHistoryReadingKind kind) => new(kind, null, []);
}
