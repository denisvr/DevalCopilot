namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

public sealed record CreateCodexPlanningAttemptCommandResult(
    Guid AttemptId, int AttemptNumber, Guid? RepairSourceAttemptId = null);
