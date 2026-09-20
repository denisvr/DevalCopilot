namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;

public sealed record EligibleReviewCorrectionAttempt(
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
    IReadOnlyList<Guid> OrderedInputMessageIds);
