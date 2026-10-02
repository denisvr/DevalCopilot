namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;

/// <summary>The bounded projection the verification-diagnosis supervisor needs to revalidate, dispatch, and invoke one
/// diagnosis attempt — never a prompt, transcript, or credential.</summary>
public sealed record EligibleVerificationDiagnosisAttempt(
    Guid AttemptId,
    Guid RunId,
    Guid GitWorkspaceId,
    string WorkspacePath,
    Guid GitCheckpointId,
    string CheckpointFingerprintSha256,
    string ContextManifestRelativeStoragePath,
    long ContextManifestByteLength,
    string ContextManifestContentHash,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes,
    string? RequestedModel,
    string? RequestedEffort);
