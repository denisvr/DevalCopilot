namespace DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;

/// <summary>How many formerly advancing sequences this recovery interrupted.</summary>
public sealed record InterruptPlanningSequencesCommandResult(int InterruptedCount);
