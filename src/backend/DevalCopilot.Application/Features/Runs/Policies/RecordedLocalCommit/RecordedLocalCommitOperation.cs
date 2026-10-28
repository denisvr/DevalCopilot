using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The proved facts of one run's recorded local-commit operation as read now (ADR-0070). Parent and tree are recorded preparation facts,
/// never proof of promotion; <see cref="CommitSha"/> exists only for a Completed operation, where it is the recorded result, and is null
/// for every other state so the prepared proposed commit identity never escapes. <see cref="Status"/> is recorded bookkeeping: Prepared
/// and Executing are markers, not an observation of a present process, and NeedsAttention is neither terminal nor success, failure or
/// permission to retry. It is not a receipt, a live Git observation, current eligibility or publication.
/// </summary>
public sealed record RecordedLocalCommitOperation(
    Guid OperationId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string CheckpointFingerprintSha256,
    LocalCommitStatus Status,
    string BranchName,
    string ParentCommitSha,
    string TreeSha,
    string? CommitSha,
    int ChangedPathCount,
    long TotalBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExecutionStartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    RecordedLocalCommitReason Reason);
