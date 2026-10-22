using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;

/// <summary>Only <see cref="LocalExecutionStepInspectionStatus.Recorded"/> carries payloads: the step and exactly one of the Agent header
/// and the verification, by the step's kind. Every other status carries none.</summary>
public sealed record GetLocalExecutionStepInspectionQueryResult(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    LocalExecutionStepInspectionStatus Status,
    LocalExecutionInspectionStep? Step,
    AgentAttemptVerificationEvidenceHeader? Agent,
    LocalExecutionVerificationInspection? Verification)
{
    public static GetLocalExecutionStepInspectionQueryResult Without(
        Guid runId, Guid sequenceId, Guid stepId, LocalExecutionStepInspectionStatus status) =>
        new(runId, sequenceId, stepId, status, null, null, null);
}
