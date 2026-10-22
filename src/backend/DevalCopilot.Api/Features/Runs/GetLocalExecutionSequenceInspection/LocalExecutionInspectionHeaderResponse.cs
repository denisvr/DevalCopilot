namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// The stored bookkeeping of the recorded sequence (ADR-0061): never effective live control, a final approval or commit proof, a preparation
/// owner token, a settings snapshot or a digest. <c>Revision</c> is a canonical Int64 string and times are UTC round-trip strings.
/// </summary>
public sealed record LocalExecutionInspectionHeaderResponse(
    Guid SequenceId,
    Guid PlanProposalMessageId,
    string PlanAuthority,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string RecordedState,
    string? ReasonCode,
    string Revision,
    int RecipeCount,
    int CorrectionAllowance,
    string ArmedAtUtc,
    string UpdatedAtUtc,
    string? EndedAtUtc,
    string AdmissionMode);
