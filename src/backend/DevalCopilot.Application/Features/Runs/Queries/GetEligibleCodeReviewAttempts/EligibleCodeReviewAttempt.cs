namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;

/// <summary>The bounded projection the code-review supervisor needs to revalidate, dispatch, and
/// invoke one code-review attempt — never a prompt, transcript, or credential. Mirrors
/// <c>EligibleChallengeResolutionAttempt</c> exactly.</summary>
public sealed record EligibleCodeReviewAttempt(
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
    int MaxTotalCapturedBytes);
