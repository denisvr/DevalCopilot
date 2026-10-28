namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The scalar facts of one run, its registered project, its associated operation and that operation's named workspace and checkpoint,
/// read by one bounded relational statement (ADR-0070). Identifiers and times are raw stored bytes within their bound, a <c>Present</c>
/// flag tells a rejected value from an absent one, <see cref="StatusCode"/> is 1 to 6 in declaration order (0 unknown), and a number is
/// null unless the stored value is an exact INTEGER in its range. Every string is read only within its byte guard and flagged when
/// oversized, and <see cref="CommitSha"/> is read only for a Completed operation. No authority member, digest, message, author, physical
/// receipt, artifact path or workspace path is part of it.
/// </summary>
internal sealed record RecordedLocalCommitRow(
    byte[]? RunId,
    byte[]? RunProjectId,
    string? StoredMode,
    byte[]? ProjectId,
    bool OperationPresent,
    byte[]? OperationId,
    byte[]? OperationRunId,
    byte[]? OperationProjectId,
    byte[]? OperationWorkspaceId,
    byte[]? OperationCheckpointId,
    int? CheckpointNumber,
    string? CheckpointFingerprint,
    bool CheckpointFingerprintOversized,
    int StatusCode,
    string? BranchName,
    bool BranchNameOversized,
    string? ParentCommitSha,
    bool ParentCommitShaOversized,
    string? TreeSha,
    bool TreeShaOversized,
    string? CommitSha,
    bool CommitShaOversized,
    int? ChangedPathCount,
    long? TotalBytes,
    byte[]? CreatedAtUtc,
    byte[]? ExecutionStartedAtUtc,
    bool ExecutionStartedPresent,
    byte[]? CompletedAtUtc,
    bool CompletedPresent,
    string? Reason,
    bool ReasonPresent,
    bool ReasonOversized,
    byte[]? WorkspaceId,
    byte[]? WorkspaceProjectId,
    string? WorkspaceBranchName,
    bool WorkspaceBranchNameOversized,
    byte[]? CheckpointId,
    byte[]? CheckpointWorkspaceId,
    int? CheckpointRowNumber,
    string? CheckpointRowFingerprint,
    bool CheckpointRowFingerprintOversized,
    string? CheckpointHeadCommitSha,
    bool CheckpointHeadCommitShaOversized);
