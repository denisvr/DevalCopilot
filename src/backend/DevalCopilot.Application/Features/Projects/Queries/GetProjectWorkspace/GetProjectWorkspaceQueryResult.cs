namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;

public sealed record GetProjectWorkspaceQueryResult(
    string PhysicalIdentityStatus,
    string? PhysicalIdentityBlockedMessage,
    WorkspacePreparationState State,
    string? CandidatePath,
    string? BranchName,
    string? SourceCommitSha,
    string? SourceBranchName,
    string? LeaseStatus,
    string? BlockedReasonCode,
    string? BlockedReasonMessage);
