using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ResolveLocalExecutionSequenceStepPreparation;

/// <summary>How the preparation of one reserved step was settled. <paramref name="Changed"/> is true only when this call recorded a
/// transition; a claim that proved persisted, or a step another writer already moved, changes nothing.</summary>
public sealed record ResolveLocalExecutionSequenceStepPreparationCommandResult(
    LocalExecutionSequenceState State, LocalExecutionSequenceStepState StepState, bool Changed);
