namespace DevalCopilot.Api.Features.Runs.RequestClaudeCriticalReviewRepairAttempt;

public sealed record RequestClaudeCriticalReviewRepairAttemptResponse(Guid AttemptId, int AttemptNumber, Guid RepairSourceAttemptId);
