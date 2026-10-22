using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionStepInspection;

/// <summary>
/// The selected operation of one recorded step (ADR-0061). <c>Status</c> is <c>Recorded</c> (the step and exactly one of the Agent header
/// and the verification, by the step's kind), <c>NoOperation</c> (only the coherent Reserved, Revoked or Unresolved shape with no recorded
/// link) or <c>Unavailable</c> (the structure or the linked operation cannot be proved). Every non-Recorded status carries null payloads.
/// </summary>
public sealed record GetLocalExecutionStepInspectionResponse(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    string Status,
    LocalExecutionInspectionStepResponse? Step,
    LocalExecutionInspectionAgentResponse? Agent,
    LocalExecutionInspectionVerificationResponse? Verification);
