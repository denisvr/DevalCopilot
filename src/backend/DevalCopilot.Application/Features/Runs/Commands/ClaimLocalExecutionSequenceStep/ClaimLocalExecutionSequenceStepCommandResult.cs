namespace DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;

/// <summary>The committed claim of one coordinator step: its operation identity (an Agent attempt or a verification execution).</summary>
public sealed record ClaimLocalExecutionSequenceStepCommandResult(Guid OperationId);
