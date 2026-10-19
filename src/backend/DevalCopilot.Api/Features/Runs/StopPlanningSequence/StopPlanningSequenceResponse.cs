namespace DevalCopilot.Api.Features.Runs.StopPlanningSequence;

/// <summary>The recorded state after "Stop after current step". <c>Changed</c> is true only when this call recorded a transition. This is
/// not process cancellation, run Stop or Pause: a claimed step still dispatches and finishes, and only successors are prohibited.
/// <c>Revision</c> is a canonical Int64 string.</summary>
public sealed record StopPlanningSequenceResponse(
    Guid SequenceId, Guid RunId, string State, string? ReasonCode, string Revision, bool Changed);
