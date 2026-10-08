namespace DevalCopilot.Api.Features.Runs.RequestLocalCommit;

/// <summary>The persisted facts of one local-commit operation. <c>CommitSha</c> is present only once the operation is Completed; it
/// names a local commit only and implies no push, no clean workspace and no provider reliability.</summary>
public sealed record LocalCommitOperationResponse(
    Guid OperationId,
    string Status,
    string? OutcomeReasonCode,
    Guid CheckpointId,
    int CheckpointNumber,
    Guid CodeReviewAttemptId,
    Guid HumanCheckpointReviewId,
    string BranchName,
    string ParentCommitSha,
    string TreeSha,
    string? CommitSha,
    int ChangedPathCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
