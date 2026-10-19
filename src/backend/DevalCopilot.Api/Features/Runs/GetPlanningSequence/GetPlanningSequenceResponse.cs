namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>The recorded status of one run's planning sequence (ADR-0055), as read now from recorded rows: never a runtime readiness
/// proof, a transcript, an artifact body, a path or a provider payload. <c>Status</c> is <c>None</c> (no sequence is recorded; <c>Arming</c>
/// holds the recorded arming availability) or the recorded or effective state name; an elapsed deadline reads as NeedsAttention
/// immediately. <c>ManualFence</c> is <c>Blocked</c>, <c>Released</c> or <c>Unavailable</c>; Released lifts only this fence and never
/// asserts that an ordinary manual operation is eligible. <c>LatestEventSequence</c> is a canonical Int64 string.</summary>
public sealed record GetPlanningSequenceResponse(
    Guid RunId,
    string Status,
    PlanningSequenceResponse? Sequence,
    PlanningSequenceArmingResponse? Arming,
    string ManualFence,
    string LatestEventSequence);
