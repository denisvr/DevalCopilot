namespace DevalCopilot.Api.Features.Projects.GetCheckpointApprovalEvidence;

public sealed record CheckpointApprovalEvidenceMemberResponse(
    Guid VerificationCommandId,
    int CommandNumber,
    string RecipeLabel,
    Guid VerificationExecutionId,
    int ExecutionNumber);
