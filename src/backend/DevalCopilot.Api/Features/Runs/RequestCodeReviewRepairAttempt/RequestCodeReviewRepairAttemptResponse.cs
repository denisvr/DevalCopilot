namespace DevalCopilot.Api.Features.Runs.RequestCodeReviewRepairAttempt;

public sealed record RequestCodeReviewRepairAttemptResponse(Guid AttemptId, int AttemptNumber, Guid RepairSourceAttemptId);
