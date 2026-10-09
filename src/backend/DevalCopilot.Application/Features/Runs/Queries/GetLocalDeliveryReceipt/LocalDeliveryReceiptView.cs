namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>
/// The version-1 historical receipt of one completed local commit: the exact evidence the operation was admitted against, read from
/// its pinned rows. It carries no path, argument, author address, message text, provider output, artifact or lock receipt, and it
/// describes the recorded delivery, never the current workspace, current eligibility or any remote publication.
/// </summary>
public sealed record LocalDeliveryReceiptView(
    int Version,
    Guid RunId,
    Guid OperationId,
    string Objective,
    string CommitSha,
    string ParentCommitSha,
    string TreeSha,
    string BranchName,
    DateTimeOffset CompletedAtUtc,
    LocalDeliveryCheckpointView Checkpoint,
    Guid ExecutionReportMessageId,
    LocalDeliveryCodeReviewView CodeReview,
    LocalDeliveryHumanReviewView HumanReview,
    IReadOnlyList<LocalDeliveryVerificationView> Verification);
