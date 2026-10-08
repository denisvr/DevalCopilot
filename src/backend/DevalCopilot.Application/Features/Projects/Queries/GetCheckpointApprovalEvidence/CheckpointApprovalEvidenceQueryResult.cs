namespace DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;

public sealed record CheckpointApprovalEvidenceQueryResult(
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string FingerprintSha256,
    IReadOnlyList<CheckpointApprovalEvidenceMemberQueryResult> Members);
