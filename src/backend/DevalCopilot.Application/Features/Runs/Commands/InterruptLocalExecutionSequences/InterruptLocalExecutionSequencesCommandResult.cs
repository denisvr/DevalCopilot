namespace DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;

/// <summary>How many formerly advancing sequences this closure interrupted and how many proved-undispatched linked claims it closed.</summary>
public sealed record InterruptLocalExecutionSequencesCommandResult(int InterruptedCount, int RefusedUndispatchedCount);
