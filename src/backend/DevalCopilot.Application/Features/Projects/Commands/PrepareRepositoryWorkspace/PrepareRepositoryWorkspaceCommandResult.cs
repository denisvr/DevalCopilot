namespace DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;

public sealed record PrepareRepositoryWorkspaceCommandResult(
    Guid WorkspaceId, string WorkspacePath, string BranchName, string SourceCommitSha, string? SourceBranchName);
