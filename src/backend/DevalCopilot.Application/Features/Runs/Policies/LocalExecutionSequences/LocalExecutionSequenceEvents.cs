using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>The bounded run events of a local execution sequence's durable transitions (ADR-0060). Payloads carry identities, fixed state
/// names, fixed kinds and fixed reason codes only; never a path, provider text, prompt, stored error or exception. SignalR stays a
/// post-commit hint.</summary>
internal static class LocalExecutionSequenceEvents
{
    public static RunEvent Armed(LocalExecutionSequence sequence, DateTimeOffset nowUtc, LocalExecutionAdmission? admission = null) =>
        sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission
            ? ArmedQueued(sequence, admission, nowUtc)
            : ArmedImmediate(sequence, nowUtc);

    /// <summary>ADR-0062: the one Human event of a queue intake, with the queued mode and the initial pending admission identity.</summary>
    private static RunEvent ArmedQueued(LocalExecutionSequence sequence, LocalExecutionAdmission? admission, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionSequenceArmed,
        ParticipantIdentity.ForHuman(),
        new
        {
            sequenceId = sequence.Id,
            workspaceId = sequence.WorkspaceId,
            checkpointId = sequence.CheckpointId,
            fingerprintSha256 = sequence.FingerprintSha256,
            planProposalMessageId = sequence.PlanProposalMessageId,
            recipeCount = sequence.RecipeCount,
            correctionAllowance = sequence.CorrectionAllowance,
            maximumSteps = LocalExecutionSequencePolicy.MaximumSteps(sequence.RecipeCount),
            admissionMode = sequence.AdmissionMode.ToString(),
            admissionId = admission?.Id,
            admissionNumber = admission?.AdmissionNumber,
        },
        nowUtc);

    private static RunEvent ArmedImmediate(LocalExecutionSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionSequenceArmed,
        ParticipantIdentity.ForHuman(),
        new
        {
            sequenceId = sequence.Id,
            workspaceId = sequence.WorkspaceId,
            checkpointId = sequence.CheckpointId,
            fingerprintSha256 = sequence.FingerprintSha256,
            planProposalMessageId = sequence.PlanProposalMessageId,
            recipeCount = sequence.RecipeCount,
            correctionAllowance = sequence.CorrectionAllowance,
            maximumSteps = LocalExecutionSequencePolicy.MaximumSteps(sequence.RecipeCount),
        },
        nowUtc);

    public static RunEvent StepReserved(LocalExecutionSequence sequence, LocalExecutionSequenceStep step, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionSequenceStepReserved,
        ParticipantIdentity.ForOrchestrator(),
        new { sequenceId = sequence.Id, stepId = step.Id, ordinal = step.Ordinal, kind = step.Kind.ToString(), pass = step.PassNumber },
        nowUtc);

    public static RunEvent StepClaimed(
        LocalExecutionSequence sequence, LocalExecutionSequenceStep step, Guid operationId, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        step.IsAgent ? operationId : null,
        RunEventType.LocalExecutionSequenceStepClaimed,
        ParticipantIdentity.ForOrchestrator(),
        new
        {
            sequenceId = sequence.Id,
            stepId = step.Id,
            ordinal = step.Ordinal,
            kind = step.Kind.ToString(),
            pass = step.PassNumber,
            operationId,
        },
        nowUtc);

    public static RunEvent StepConcluded(
        LocalExecutionSequence sequence, LocalExecutionSequenceStep step, string operationStatus, string? outcome, DateTimeOffset nowUtc) =>
        Record(
            sequence.RunId,
            step.IsAgent ? step.OperationId : null,
            RunEventType.LocalExecutionSequenceStepConcluded,
            ParticipantIdentity.ForOrchestrator(),
            new
            {
                sequenceId = sequence.Id,
                stepId = step.Id,
                ordinal = step.Ordinal,
                kind = step.Kind.ToString(),
                operationStatus,
                outcome,
            },
            nowUtc);

    public static RunEvent AdmissionQueued(LocalExecutionSequence sequence, LocalExecutionAdmission admission, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionAdmissionQueued,
        ParticipantIdentity.ForOrchestrator(),
        new
        {
            sequenceId = sequence.Id,
            admissionId = admission.Id,
            admissionNumber = admission.AdmissionNumber,
            ordinal = admission.Ordinal,
            kind = admission.Kind.ToString(),
            pass = admission.PassNumber,
        },
        nowUtc);

    public static RunEvent AdmissionWaiting(LocalExecutionSequence sequence, LocalExecutionAdmission admission, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionAdmissionWaiting,
        ParticipantIdentity.ForOrchestrator(),
        new
        {
            sequenceId = sequence.Id,
            admissionId = admission.Id,
            admissionNumber = admission.AdmissionNumber,
            reason = admission.WaitingReason,
        },
        nowUtc);

    public static RunEvent StopRequested(LocalExecutionSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionSequenceStopRequested,
        ParticipantIdentity.ForHuman(),
        new { sequenceId = sequence.Id, state = sequence.State.ToString() },
        nowUtc);

    public static RunEvent Ended(LocalExecutionSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.LocalExecutionSequenceEnded,
        ParticipantIdentity.ForOrchestrator(),
        new
        {
            sequenceId = sequence.Id,
            state = sequence.State.ToString(),
            reason = sequence.ReasonCode,
            detail = sequence.DetailCode,
            finalReportMessageId = sequence.FinalReportMessageId,
            finalReviewAttemptId = sequence.FinalReviewAttemptId,
            finalEscalationMessageId = sequence.FinalEscalationMessageId,
        },
        nowUtc);

    private static RunEvent Record(
        Guid runId, Guid? attemptId, string eventType, ParticipantIdentity actor, object payload, DateTimeOffset nowUtc) =>
        RunEvent.Record(Guid.NewGuid(), runId, attemptId, eventType, actor, JsonSerializer.Serialize(payload), nowUtc);
}
