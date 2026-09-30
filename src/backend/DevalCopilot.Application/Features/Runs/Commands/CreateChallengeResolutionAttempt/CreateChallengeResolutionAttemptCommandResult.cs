namespace DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;

public sealed record CreateChallengeResolutionAttemptCommandResult(Guid AttemptId, int AttemptNumber, Guid? RepairSourceAttemptId = null);
