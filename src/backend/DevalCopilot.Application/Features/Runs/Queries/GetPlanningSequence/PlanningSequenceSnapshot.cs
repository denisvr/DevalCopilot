using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>The recorded sequence. <see cref="State"/> is the effective state: an elapsed deadline reads as NeedsAttention
/// immediately, with <see cref="ObservedBeforePersisted"/> true until the coordinator persists it; <see cref="RecordedState"/> is what is
/// stored.</summary>
public sealed record PlanningSequenceSnapshot(
    Guid SequenceId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    PlanningSequenceState State,
    PlanningSequenceState RecordedState,
    bool ObservedBeforePersisted,
    string? ReasonCode,
    string? DetailCode,
    string? NextActionCode,
    long Revision,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StopRequestedAtUtc,
    DateTimeOffset? EndedAtUtc,
    bool AwaitingReconciliation,
    Guid? OutstandingAttemptId,
    IReadOnlyList<PlanningSequenceStepReceipt> Steps,
    PlanningSequenceFinalReferences? Final);
