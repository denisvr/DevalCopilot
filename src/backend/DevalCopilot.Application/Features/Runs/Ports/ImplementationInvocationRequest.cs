namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything the Claude implementation adapter needs to invoke one already-claimed,
/// already-dispatched initial implementation attempt. <see cref="LaunchExecutablePath"/> is already
/// resolved and revalidated by the caller from the durable Claude capability snapshot — this
/// request never causes a new search through PATH or a private provider install location. There
/// is no <c>LaunchScriptPath</c>: the Claude launch target is always a direct executable. The
/// <see cref="WorkspacePath"/> is the owned worktree and the only directory the process may read or edit.
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
