namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The outcome of reading one run's recorded sequence structure. <see cref="SequenceId"/> is the stored identity of a present
/// sequence row when it could be parsed, even when the structure cannot be proved.</summary>
internal sealed record PlanningSequenceInspectionReading(
    PlanningSequenceInspectionReadingKind Kind, Guid? SequenceId, PlanningSequenceInspectionStructure? Structure)
{
    public static readonly PlanningSequenceInspectionReading RunNotFound = new(PlanningSequenceInspectionReadingKind.RunNotFound, null, null);

    public static readonly PlanningSequenceInspectionReading NotRecorded = new(PlanningSequenceInspectionReadingKind.NotRecorded, null, null);

    public static PlanningSequenceInspectionReading Unavailable(Guid? sequenceId) =>
        new(PlanningSequenceInspectionReadingKind.Unavailable, sequenceId, null);

    public static PlanningSequenceInspectionReading Recorded(PlanningSequenceInspectionStructure structure) =>
        new(PlanningSequenceInspectionReadingKind.Recorded, structure.Header.SequenceId, structure);
}
