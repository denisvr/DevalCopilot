using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;

/// <summary>The recorded state after a Stop request. <paramref name="Changed"/> is true only when this call recorded a transition; a
/// repeated request or a sequence that already ended reports the recorded state unchanged.</summary>
public sealed record StopLocalExecutionSequenceCommandResult(
    Guid SequenceId, Guid RunId, LocalExecutionSequenceState State, string? ReasonCode, long Revision, bool Changed);
