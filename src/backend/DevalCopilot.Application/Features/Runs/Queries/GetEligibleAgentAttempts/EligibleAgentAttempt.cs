namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;

/// <summary>The bounded projection the Agent attempt supervisor needs to revalidate, dispatch,
/// and invoke one Codex planning attempt — never a prompt, transcript, or credential.</summary>
public sealed record EligibleAgentAttempt(
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
