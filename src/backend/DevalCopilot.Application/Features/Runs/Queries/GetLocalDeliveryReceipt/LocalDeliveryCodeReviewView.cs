namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>The pinned CodeReviewer attempt and its own approval message, not a bare Agent checkpoint review.</summary>
public sealed record LocalDeliveryCodeReviewView(Guid AttemptId, int AttemptNumber, Guid ApprovalMessageId);
