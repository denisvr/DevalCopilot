namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;

/// <summary>The bounded projection the challenge-resolution supervisor needs to revalidate,
/// dispatch, and invoke one challenge-resolution attempt — never a prompt, transcript, or
/// credential. Mirrors <c>EligibleClaudeCriticalReviewAttempt</c> exactly, plus the ordered
/// Challenge message identities every challenge-resolution attempt resolves.</summary>
public sealed record EligibleChallengeResolutionAttempt(
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
    Guid OriginalProposalMessageId,
    IReadOnlyList<Guid> ChallengeMessageIds);
