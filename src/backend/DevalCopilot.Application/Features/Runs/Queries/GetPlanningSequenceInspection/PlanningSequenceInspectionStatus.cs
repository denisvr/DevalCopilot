namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;

/// <summary><see cref="NotRecorded"/>: the known run has no sequence. <see cref="Unavailable"/>: a sequence is present but its structure
/// cannot be proved or changed during the read, and nothing of it is shown. <see cref="Recorded"/>: the header and every step.</summary>
public enum PlanningSequenceInspectionStatus
{
    NotRecorded,
    Recorded,
    Unavailable,
}
