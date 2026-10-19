namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>The recorded sequence and its immutable pinned tuple (never a replacement target). <c>State</c> is the effective state and
/// <c>RecordedState</c> the stored one; <c>ObservedBeforePersisted</c> is true while an elapsed deadline is reported before the
/// coordinator persisted it. Reasons and next actions are fixed codes with fixed text. <c>Revision</c> is a canonical Int64 string and
/// every time a UTC round-trip string. A claim or dispatch marker in a step is bookkeeping and never proof of a live process.</summary>
public sealed record PlanningSequenceResponse(
    Guid SequenceId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string State,
    string RecordedState,
    bool ObservedBeforePersisted,
    string? ReasonCode,
    string? ReasonText,
    string? DetailCode,
    string NextActionCode,
    string NextActionText,
    string Revision,
    string ArmedAtUtc,
    string UpdatedAtUtc,
    string? StopRequestedAtUtc,
    string? EndedAtUtc,
    bool AwaitingReconciliation,
    Guid? OutstandingAttemptId,
    int MaximumSteps,
    IReadOnlyList<PlanningSequenceStepResponse> Steps,
    PlanningSequenceFinalResponse? Final);
