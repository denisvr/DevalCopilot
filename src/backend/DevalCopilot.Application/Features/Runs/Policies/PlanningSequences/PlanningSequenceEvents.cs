using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>The bounded run events of a planning sequence's durable transitions (ADR-0055). Payloads carry identities, fixed state names
/// and fixed reason codes only; never a path, provider text, prompt, stored error or exception. SignalR stays a post-commit hint.</summary>
internal static class PlanningSequenceEvents
{
    public static RunEvent Armed(PlanningSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.PlanningSequenceArmed,
        ParticipantIdentity.ForHuman(),
        new
        {
            sequenceId = sequence.Id,
            workspaceId = sequence.WorkspaceId,
            checkpointId = sequence.CheckpointId,
            fingerprintSha256 = sequence.FingerprintSha256,
            maximumSteps = PlanningSequencePolicy.MaximumSteps,
        },
        nowUtc);

    public static RunEvent StepReserved(PlanningSequence sequence, PlanningSequenceStep step, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.PlanningSequenceStepReserved,
        ParticipantIdentity.ForOrchestrator(),
        new { sequenceId = sequence.Id, stepId = step.Id, ordinal = step.Ordinal, role = step.Role.ToString() },
        nowUtc);

    public static RunEvent StepClaimed(PlanningSequence sequence, PlanningSequenceStep step, Guid attemptId, DateTimeOffset nowUtc) =>
        Record(
            sequence.RunId,
            attemptId,
            RunEventType.PlanningSequenceStepClaimed,
            ParticipantIdentity.ForOrchestrator(),
            new { sequenceId = sequence.Id, stepId = step.Id, ordinal = step.Ordinal, role = step.Role.ToString(), attemptId },
            nowUtc);

    public static RunEvent StepConcluded(
        PlanningSequence sequence, PlanningSequenceStep step, AttemptStatus attemptStatus, AgentOutcome? outcome, DateTimeOffset nowUtc) =>
        Record(
            sequence.RunId,
            step.AttemptId,
            RunEventType.PlanningSequenceStepConcluded,
            ParticipantIdentity.ForOrchestrator(),
            new
            {
                sequenceId = sequence.Id,
                stepId = step.Id,
                ordinal = step.Ordinal,
                attemptStatus = attemptStatus.ToString(),
                outcome = outcome?.ToString(),
            },
            nowUtc);

    public static RunEvent StopRequested(PlanningSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.PlanningSequenceStopRequested,
        ParticipantIdentity.ForHuman(),
        new { sequenceId = sequence.Id, state = sequence.State.ToString() },
        nowUtc);

    public static RunEvent Ended(PlanningSequence sequence, DateTimeOffset nowUtc) => Record(
        sequence.RunId,
        null,
        RunEventType.PlanningSequenceEnded,
        ParticipantIdentity.ForOrchestrator(),
        new
        {
            sequenceId = sequence.Id,
            state = sequence.State.ToString(),
            reason = sequence.ReasonCode,
            detail = sequence.DetailCode,
        },
        nowUtc);

    private static RunEvent Record(
        Guid runId, Guid? attemptId, string eventType, ParticipantIdentity actor, object payload, DateTimeOffset nowUtc) =>
        RunEvent.Record(Guid.NewGuid(), runId, attemptId, eventType, actor, JsonSerializer.Serialize(payload), nowUtc);
}
