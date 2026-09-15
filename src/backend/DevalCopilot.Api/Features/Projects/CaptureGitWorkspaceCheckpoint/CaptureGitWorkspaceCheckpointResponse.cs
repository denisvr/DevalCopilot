namespace DevalCopilot.Api.Features.Projects.CaptureGitWorkspaceCheckpoint;

public sealed record CaptureGitWorkspaceCheckpointResponse(
    Guid CheckpointId, int CheckpointNumber, string HeadCommitSha, string FingerprintSha256, int ChangedFileCount);
