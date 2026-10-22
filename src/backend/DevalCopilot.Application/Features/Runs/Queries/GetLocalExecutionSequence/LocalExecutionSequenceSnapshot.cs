using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

/// <summary>The recorded sequence. <see cref="State"/> is the effective state: an elapsed deadline reads as NeedsAttention immediately,
/// with <see cref="ObservedBeforePersisted"/> true until the coordinator persists it; <see cref="RecordedState"/> is what is stored.
/// <see cref="CurrentCheckpointId"/> is the source of the newest step, not proof that the workspace still holds it.</summary>
public sealed record LocalExecutionSequenceSnapshot(
    Guid SequenceId,
    Guid PlanProposalMessageId,
    LocalExecutionPlanAuthority PlanAuthority,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    Guid CurrentCheckpointId,
    string CurrentFingerprintSha256,
    LocalExecutionSequenceState State,
    LocalExecutionSequenceState RecordedState,
    bool ObservedBeforePersisted,
    string? ReasonCode,
    string? DetailCode,
    string NextActionCode,
    long Revision,
    int RecipeCount,
    int MaximumSteps,
    int CorrectionAllowance,
    int CorrectionsClaimed,
    int PassesStarted,
    int AgentClaims,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset AdvancementDeadlineUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StopRequestedAtUtc,
    DateTimeOffset? EndedAtUtc,
    bool AwaitingReconciliation,
    Guid? OutstandingOperationId,
    IReadOnlyList<LocalExecutionSequenceStepReceipt> Steps,
    LocalExecutionSequenceFinalReferences? Final,
    LocalExecutionAdmissionMode AdmissionMode = LocalExecutionAdmissionMode.Immediate,
    LocalExecutionQueueDisclosure? Queue = null);
