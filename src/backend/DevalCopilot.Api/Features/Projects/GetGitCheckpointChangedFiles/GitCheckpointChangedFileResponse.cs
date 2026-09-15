namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointChangedFiles;

public sealed record GitCheckpointChangedFileResponse(
    string Path, string? PreviousPath, string IndexStatus, string WorkTreeStatus);
