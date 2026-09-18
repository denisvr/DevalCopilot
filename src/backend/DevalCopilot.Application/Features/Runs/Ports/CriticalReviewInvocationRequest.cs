namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICriticalReviewAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Claude critical-review attempt. <see cref="LaunchExecutablePath"/> is
/// already resolved and revalidated by the caller from the durable Claude capability snapshot —
/// this request never causes a new search through PATH or a private provider install location.
/// There is no script-path component: the Infrastructure adapter only ever accepts a
/// <c>DirectExecutable</c> launch target for Claude, never a Node-hosted script.
/// </summary>
public sealed record CriticalReviewInvocationRequest(
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
