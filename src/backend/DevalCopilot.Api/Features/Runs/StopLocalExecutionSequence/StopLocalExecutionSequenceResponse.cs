namespace DevalCopilot.Api.Features.Runs.StopLocalExecutionSequence;

/// <summary>The recorded state after "Stop after current operation". <c>Changed</c> is true only when this call recorded a transition.
/// This is not process cancellation, run Stop or Pause: a claimed operation still dispatches and finishes (subject to every independent
/// gate and its deadline), and only successors are prohibited. <c>Revision</c> is a canonical Int64 string.</summary>
public sealed record StopLocalExecutionSequenceResponse(
    Guid SequenceId, Guid RunId, string State, string? ReasonCode, string Revision, bool Changed);
