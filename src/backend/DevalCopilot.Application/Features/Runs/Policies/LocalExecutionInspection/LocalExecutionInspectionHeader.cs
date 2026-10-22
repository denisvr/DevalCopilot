using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The stored bookkeeping of the recorded sequence (ADR-0061): never effective live control, a final approval or commit proof, a
/// preparation owner token, a settings snapshot or a digest.</summary>
public sealed record LocalExecutionInspectionHeader(
    Guid SequenceId,
    Guid PlanProposalMessageId,
    LocalExecutionPlanAuthority PlanAuthority,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    LocalExecutionSequenceState RecordedState,
    string? ReasonCode,
    long Revision,
    int RecipeCount,
    int CorrectionAllowance,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? EndedAtUtc);
