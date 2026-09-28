namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICodexChallengeResolutionAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Codex challenge-resolution attempt. <see cref="LaunchExecutablePath"/> and
/// <see cref="LaunchScriptPath"/> are already resolved and revalidated by the caller from the
/// durable Codex capability snapshot — this request never causes a new search through PATH or a
/// private provider install location. Mirrors <c>CodexPlanningInvocationRequest</c> exactly,
/// including <see cref="RequestedModel"/>/<see cref="RequestedEffort"/>: this claimed attempt's
/// own immutable requested assignment, never a later mutable Run value.
/// </summary>
public sealed record ChallengeResolutionInvocationRequest(
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
