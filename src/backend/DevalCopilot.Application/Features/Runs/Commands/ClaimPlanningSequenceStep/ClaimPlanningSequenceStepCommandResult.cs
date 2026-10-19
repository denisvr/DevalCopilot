namespace DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;

/// <summary>The committed claim of one coordinator step: its attempt identity and number.</summary>
public sealed record ClaimPlanningSequenceStepCommandResult(Guid AttemptId, int AttemptNumber);
