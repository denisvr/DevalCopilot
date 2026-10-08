using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The safe, persisted facts of one local-commit operation that the command result and the status query expose. It never carries
/// the commit message text beyond what the human wrote, a filesystem path, Git output or an exception. The commit SHA is shown
/// only once the operation is Completed; before that it is a recorded intention, not a delivery.
/// </summary>
public sealed record LocalCommitOperationView(
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
    DateTimeOffset? CompletedAtUtc)
{
    public static LocalCommitOperationView From(LocalCommitOperation operation) => new(
        operation.Id,
        operation.Status.ToString(),
        operation.OutcomeReasonCode,
        operation.GitCheckpointId,
        operation.CheckpointNumber,
        operation.CodeReviewAttemptId,
        operation.HumanCheckpointReviewId,
        operation.BranchName,
        operation.ParentCommitSha,
        operation.TreeSha,
        operation.Status == LocalCommitStatus.Completed ? operation.CommitSha : null,
        operation.ChangedPathCount,
        operation.CreatedAtUtc,
        operation.CompletedAtUtc);
}
