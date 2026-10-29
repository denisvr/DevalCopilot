namespace DevalCopilot.Api.Features.Runs.GetRunAbandonmentInspection;

/// <summary>
/// The closed graph of one run's recorded abandonment inspection (ADR-0071). <c>Status</c> is exactly <c>Recorded</c>,
/// <c>NotRecorded</c> or <c>Unavailable</c>; <c>Abandonment</c> is present only for <c>Recorded</c>. NotRecorded means only that a
/// recognized non-Abandoned run has no recorded abandonment fact in this read (it says nothing about eligibility, ownership or another
/// objective) and Unavailable that recorded abandonment markers or events could not pass the selected proof.
/// </summary>
public sealed record GetRunAbandonmentInspectionResponse(Guid RunId, string Status, RunAbandonmentInspectionRecordedResponse? Abandonment);
