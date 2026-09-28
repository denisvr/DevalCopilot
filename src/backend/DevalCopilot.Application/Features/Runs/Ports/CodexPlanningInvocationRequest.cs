namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICodexPlanningAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Codex planning attempt. <see cref="LaunchExecutablePath"/> and
/// <see cref="LaunchScriptPath"/> are already resolved and revalidated by the caller from the
/// durable Codex capability snapshot — this request never causes a new search through PATH or a
/// private provider install location. <see cref="RequestedModel"/> and
/// <see cref="RequestedEffort"/> are this claimed attempt's own immutable requested assignment
/// (never a later, possibly different, mutable Run value) and are already bounded, validated
/// identifiers by the time they reach here — never a raw catalog payload.
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
    int MaxTotalCapturedBytes,
    string? RequestedModel = null,
    string? RequestedEffort = null);
