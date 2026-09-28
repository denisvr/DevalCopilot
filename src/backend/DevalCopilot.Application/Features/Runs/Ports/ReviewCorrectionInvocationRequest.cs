namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary><see cref="RequestedClaudeModel"/> is this claimed attempt's own immutable model-alias
/// request (never a later, possibly different, mutable Run value); <see langword="null"/> means no override.</summary>
public sealed record ReviewCorrectionInvocationRequest(
    Guid RunId,
    Guid AttemptId,
    string WorkspacePath,
    string ContextManifestRelativeStoragePath,
    long ContextManifestByteLength,
    string ContextManifestContentHash,
    string LaunchExecutablePath,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes,
    string? RequestedClaudeModel = null);
