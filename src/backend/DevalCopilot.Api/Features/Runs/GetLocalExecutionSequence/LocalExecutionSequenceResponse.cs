namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>The recorded sequence and its immutable pinned tuple (never a replacement target). <c>State</c> is the effective state and
/// <c>RecordedState</c> the stored one; <c>ObservedBeforePersisted</c> is true while an elapsed deadline is reported before the
/// coordinator persisted it. <c>CurrentCheckpointId</c> is the source of the newest step, never proof that the workspace still holds it.
/// Reasons and next actions are fixed codes with fixed text. <c>Revision</c> is a canonical Int64 string and every time a UTC round-trip
/// string. A claim, a dispatch marker or a recorded exit in a step is bookkeeping and never proof of a live process or a stopped one.</summary>
public sealed record LocalExecutionSequenceResponse(
    Guid SequenceId,
    Guid PlanProposalMessageId,
    string PlanAuthority,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    Guid CurrentCheckpointId,
    string CurrentFingerprintSha256,
    string State,
    string RecordedState,
    bool ObservedBeforePersisted,
    string? ReasonCode,
    string? ReasonText,
    string? DetailCode,
    string NextActionCode,
    string NextActionText,
    string Revision,
    int RecipeCount,
    int MaximumSteps,
    int CorrectionAllowance,
    int CorrectionsClaimed,
    int PassesStarted,
    int AgentClaims,
    string ArmedAtUtc,
    string AdvancementDeadlineUtc,
    string UpdatedAtUtc,
    string? StopRequestedAtUtc,
    string? EndedAtUtc,
    bool AwaitingReconciliation,
    Guid? OutstandingOperationId,
    IReadOnlyList<LocalExecutionSequenceStepResponse> Steps,
    LocalExecutionSequenceFinalResponse? Final);
