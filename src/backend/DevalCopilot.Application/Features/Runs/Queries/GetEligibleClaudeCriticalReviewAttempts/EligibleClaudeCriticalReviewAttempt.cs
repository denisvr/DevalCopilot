namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;

/// <summary>The bounded projection the Claude critical-review supervisor needs to revalidate,
/// dispatch, and invoke one critical-review attempt — never a prompt, transcript, or credential.
/// Mirrors <c>EligibleAgentAttempt</c> exactly, plus the exact reviewed Proposal message
/// identity every critical-review attempt carries.</summary>
public sealed record EligibleClaudeCriticalReviewAttempt(
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
    Guid InputCollaborationMessageId);
