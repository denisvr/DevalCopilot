namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>Everything the adapter needs to invoke one already-claimed, already-dispatched diagnosis attempt.
/// <see cref="RequestedModel"/>/<see cref="RequestedEffort"/> are the attempt's own immutable requested assignment.</summary>
public sealed record VerificationDiagnosisInvocationRequest(
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
