namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

/// <summary>The version-1 historical receipt. Every value is a fact recorded when the local commit was admitted and completed.</summary>
public sealed record LocalDeliveryReceiptResponse(
    int Version,
    Guid RunId,
    Guid OperationId,
    string Objective,
    string CommitSha,
    string ParentCommitSha,
    string TreeSha,
    string BranchName,
    DateTimeOffset CompletedAtUtc,
    LocalDeliveryCheckpointResponse Checkpoint,
    Guid ExecutionReportMessageId,
    LocalDeliveryCodeReviewResponse CodeReview,
    LocalDeliveryHumanReviewResponse HumanReview,
    IReadOnlyList<LocalDeliveryVerificationResponse> Verification);
