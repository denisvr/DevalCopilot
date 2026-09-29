namespace DevalCopilot.Api.Features.Runs.RequestCodexPlanningRepairAttempt;

public sealed record RequestCodexPlanningRepairAttemptResponse(Guid AttemptId, int AttemptNumber, Guid RepairSourceAttemptId);
