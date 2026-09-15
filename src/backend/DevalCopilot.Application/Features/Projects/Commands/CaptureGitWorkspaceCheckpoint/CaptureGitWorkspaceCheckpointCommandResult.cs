namespace DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;

public sealed record CaptureGitWorkspaceCheckpointCommandResult(
    Guid CheckpointId, int CheckpointNumber, string HeadCommitSha, string FingerprintSha256, int ChangedFileCount);
