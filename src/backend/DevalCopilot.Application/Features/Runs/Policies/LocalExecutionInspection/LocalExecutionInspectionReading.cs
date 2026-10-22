namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The outcome of reading one run's recorded sequence structure. <see cref="SequenceId"/> is the stored identity of a present
/// sequence row even when its structure cannot be proved.</summary>
internal sealed record LocalExecutionInspectionReading(
    LocalExecutionInspectionReadingKind Kind, Guid? SequenceId, LocalExecutionInspectionStructure? Structure)
{
    public static readonly LocalExecutionInspectionReading RunNotFound = new(LocalExecutionInspectionReadingKind.RunNotFound, null, null);

    public static readonly LocalExecutionInspectionReading NotRecorded = new(LocalExecutionInspectionReadingKind.NotRecorded, null, null);

    public static LocalExecutionInspectionReading Unavailable(Guid? sequenceId) =>
        new(LocalExecutionInspectionReadingKind.Unavailable, sequenceId, null);

    public static LocalExecutionInspectionReading Recorded(LocalExecutionInspectionStructure structure) =>
        new(LocalExecutionInspectionReadingKind.Recorded, structure.Header.SequenceId, structure);
}
