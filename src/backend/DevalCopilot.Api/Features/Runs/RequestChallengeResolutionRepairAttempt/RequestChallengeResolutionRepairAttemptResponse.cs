namespace DevalCopilot.Api.Features.Runs.RequestChallengeResolutionRepairAttempt;

public sealed record RequestChallengeResolutionRepairAttemptResponse(Guid AttemptId, int AttemptNumber, Guid RepairSourceAttemptId);
