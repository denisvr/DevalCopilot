namespace DevalCopilot.Api.Features.Projects.GetProjectWorkspace;

public sealed record GetProjectWorkspaceResponse(
    string PhysicalIdentityStatus,
    string? PhysicalIdentityBlockedMessage,
    string State,
    string? CandidatePath,
    string? BranchName,
    string? SourceCommitSha,
    string? SourceBranchName,
    string? LeaseStatus,
    string? BlockedReasonCode,
    string? BlockedReasonMessage);
