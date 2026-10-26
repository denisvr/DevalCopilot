using DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

namespace DevalCopilot.Api.Features.Runs.GetPlanningStepInspection;

/// <summary>
/// The selected association of one recorded planning step (ADR-0068). <c>Status</c> is <c>Recorded</c> (the step and its Agent identity),
/// <c>NoLinkedAttempt</c> (only the coherent Reserved, Revoked or Unresolved shape with no recorded attempt link; it says nothing about
/// whether preparation or execution occurred) or <c>Unavailable</c> (the structure or the linked attempt cannot be proved, or the sequence
/// changed during the read). Every non-Recorded status carries null payloads.
/// </summary>
public sealed record GetPlanningStepInspectionResponse(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    string Status,
    PlanningSequenceInspectionStepResponse? Step,
    PlanningStepInspectionAgentResponse? Agent);
