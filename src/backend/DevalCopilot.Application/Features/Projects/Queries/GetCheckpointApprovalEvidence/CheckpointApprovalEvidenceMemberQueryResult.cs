namespace DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;

public sealed record CheckpointApprovalEvidenceMemberQueryResult(
    Guid VerificationCommandId,
    int CommandNumber,
    string RecipeLabel,
    Guid VerificationExecutionId,
    int ExecutionNumber);
