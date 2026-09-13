namespace DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;

public sealed record ClaimProcessAttemptCommandResult(Guid AttemptId, int AttemptNumber);
