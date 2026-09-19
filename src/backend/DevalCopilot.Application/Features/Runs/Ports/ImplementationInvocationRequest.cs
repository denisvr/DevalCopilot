namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="IClaudeImplementationAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Claude implementation attempt. <see cref="LaunchExecutablePath"/> is already
/// resolved and revalidated by the caller from the durable Claude capability snapshot — this
/// request never causes a new search through PATH or a private provider install location. There
/// is no <c>LaunchScriptPath</c>: the Claude launch target is always a direct executable.
/// <see cref="WorkspacePath"/> is the owned worktree and is the only directory the invoked process
/// may read or edit.
/// </summary>
public sealed record ImplementationInvocationRequest(
    Guid RunId,
    Guid AttemptId,
    string WorkspacePath,
    string ContextManifestRelativeStoragePath,
    long ContextManifestByteLength,
    string ContextManifestContentHash,
    string LaunchExecutablePath,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes);
