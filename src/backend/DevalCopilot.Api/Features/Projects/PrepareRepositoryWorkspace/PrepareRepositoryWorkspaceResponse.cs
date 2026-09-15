namespace DevalCopilot.Api.Features.Projects.PrepareRepositoryWorkspace;

public sealed record PrepareRepositoryWorkspaceResponse(
    Guid WorkspaceId, string WorkspacePath, string BranchName, string SourceCommitSha, string? SourceBranchName);
