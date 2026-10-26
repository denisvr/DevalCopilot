namespace DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

/// <summary>
/// The recorded index of one run's planning sequence (ADR-0068). <c>Status</c> is <c>NotRecorded</c> (a known run with no sequence),
/// <c>Unavailable</c> (a present sequence whose structure cannot be proved or changed during the read) or <c>Recorded</c>. Only
/// <c>Recorded</c> carries the header and the complete step list of at most five members; every other status carries a null header and an
/// empty list.
/// </summary>
public sealed record GetPlanningSequenceInspectionResponse(
    Guid RunId,
    string Status,
    PlanningSequenceInspectionHeaderResponse? Sequence,
    IReadOnlyList<PlanningSequenceInspectionStepResponse> Steps);
