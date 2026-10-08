namespace DevalCopilot.Api.Features.Projects.GetCheckpointApprovalEvidence;

public sealed record CheckpointApprovalEvidenceResponse(
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string FingerprintSha256,
    IReadOnlyList<CheckpointApprovalEvidenceMemberResponse> Members);
