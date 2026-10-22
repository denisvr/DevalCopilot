using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;

/// <summary>The recorded receipt of an arming: the immutable tuple, the current state and whether this answer replays an earlier
/// arming. It proves no runtime readiness and no provider, verification or process activity.</summary>
public sealed record ArmLocalExecutionSequenceCommandResult(
    Guid SequenceId,
    Guid RunId,
    Guid PlanProposalMessageId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string ConfirmationSha256,
    LocalExecutionSequenceState State,
    int RecipeCount,
    int CorrectionAllowance,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset AdvancementDeadlineUtc,
    long Revision,
    bool Replayed,
    LocalExecutionAdmissionMode AdmissionMode = LocalExecutionAdmissionMode.Immediate,
    LocalExecutionQueueDisclosure? Queue = null);
