namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Everything <see cref="ICodexImplementationReviewAdapter"/> needs to invoke one already-claimed,
/// already-dispatched Codex implementation-review attempt. <see cref="LaunchExecutablePath"/> and
/// <see cref="LaunchScriptPath"/> are already resolved and revalidated by the caller from the
/// durable Codex capability snapshot — this request never causes a new search through PATH or a
/// private provider install location. Mirrors <c>ChallengeResolutionInvocationRequest</c> exactly.
/// This is a read-only review: <see cref="WorkspacePath"/> is provided only so the adapter can
/// pass it as the process working directory for the already-bounded, already-read-only Codex
/// launch contract — never so the adapter can grant itself broader tool access than that contract
/// already allows.
/// </summary>
public sealed record ImplementationReviewInvocationRequest(
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
