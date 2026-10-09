namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

public sealed record LocalDeliveryCodeReviewResponse(Guid AttemptId, int AttemptNumber, Guid ApprovalMessageId);
