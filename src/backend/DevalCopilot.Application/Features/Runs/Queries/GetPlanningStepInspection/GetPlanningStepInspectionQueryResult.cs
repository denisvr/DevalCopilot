using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;

/// <summary>Only <see cref="PlanningStepInspectionStatus.Recorded"/> carries payloads: the step and the existing immutable Agent header.
/// Every other status carries none.</summary>
public sealed record GetPlanningStepInspectionQueryResult(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    PlanningStepInspectionStatus Status,
    PlanningSequenceInspectionStep? Step,
    AgentAttemptVerificationEvidenceHeader? Agent)
{
    public static GetPlanningStepInspectionQueryResult Without(
        Guid runId, Guid sequenceId, Guid stepId, PlanningStepInspectionStatus status) =>
        new(runId, sequenceId, stepId, status, null, null);
}
