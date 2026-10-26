namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;

/// <summary><see cref="Recorded"/>: the step and its proved Agent identity. <see cref="NoLinkedAttempt"/>: only the coherent Reserved,
/// Revoked or Unresolved shape with no recorded attempt link; it says no link is recorded, never that preparation or execution could not
/// have occurred. <see cref="Unavailable"/>: the structure or the linked attempt cannot be proved, or the sequence changed during the
/// read.</summary>
public enum PlanningStepInspectionStatus
{
    Recorded,
    NoLinkedAttempt,
    Unavailable,
}
