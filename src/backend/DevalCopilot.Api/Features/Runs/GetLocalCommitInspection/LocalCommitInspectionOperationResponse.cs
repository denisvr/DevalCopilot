namespace DevalCopilot.Api.Features.Runs.GetLocalCommitInspection;

/// <summary>
/// The recorded facts of one local-commit operation as read now (ADR-0070). <c>Status</c> is one of <c>Prepared</c>, <c>Executing</c>,
/// <c>Completed</c>, <c>Failed</c>, <c>Interrupted</c> or <c>NeedsAttention</c> and is recorded bookkeeping: Prepared and Executing are
/// markers, not an observation of a present process, and NeedsAttention is neither terminal nor success, failure or permission to retry.
/// <c>ParentCommitSha</c> and <c>TreeSha</c> are recorded preparation facts, never proof of promotion. <c>CommitSha</c> is present only
/// for <c>Completed</c> and is null for every other state. <c>ExecutionStartedAtUtc</c> is null when the single-use marker was never
/// recorded and <c>CompletedAtUtc</c> when no completion was recorded; times are UTC round-trip strings.
/// </summary>
public sealed record LocalCommitInspectionOperationResponse(
    Guid OperationId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string CheckpointFingerprintSha256,
    string Status,
    string BranchName,
    string ParentCommitSha,
    string TreeSha,
    string? CommitSha,
    int ChangedPathCount,
    int TotalBytes,
    string CreatedAtUtc,
    string? ExecutionStartedAtUtc,
    string? CompletedAtUtc,
    LocalCommitInspectionReasonResponse Reason);
