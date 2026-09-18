namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICodexPlanningAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Codex planning attempt. <see cref="LaunchExecutablePath"/> and
/// <see cref="LaunchScriptPath"/> are already resolved and revalidated by the caller from the
/// durable Codex capability snapshot — this request never causes a new search through PATH or a
/// private provider install location.
/// </summary>
public sealed record CodexPlanningInvocationRequest(
    Guid RunId,
    Guid AttemptId,
    string WorkspacePath,
    string ContextManifestRelativeStoragePath,
    long ContextManifestByteLength,
    string ContextManifestContentHash,
    string LaunchExecutablePath,
    string? LaunchScriptPath,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes);
