namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICriticalReviewAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Claude critical-review attempt. <see cref="LaunchExecutablePath"/> is
/// already resolved and revalidated by the caller from the durable Claude capability snapshot —
/// this request never causes a new search through PATH or a private provider install location.
/// There is no script-path component: the Infrastructure adapter only ever accepts a
/// <c>DirectExecutable</c> launch target for Claude, never a Node-hosted script.
/// <see cref="RequestedClaudeModel"/> is this claimed attempt's own immutable model-alias and effort request
/// (never a later, possibly different, mutable Run value); <see langword="null"/> means no override. <see cref="RequestedClaudeEffort"/> is the paired, optional immutable effort request.
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
    int MaxTotalCapturedBytes,
    string? RequestedClaudeModel = null,
    string? RequestedClaudeEffort = null);
