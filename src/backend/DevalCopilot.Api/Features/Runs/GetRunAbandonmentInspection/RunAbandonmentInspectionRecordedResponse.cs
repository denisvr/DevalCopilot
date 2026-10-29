namespace DevalCopilot.Api.Features.Runs.GetRunAbandonmentInspection;

/// <summary>
/// The recorded Human abandonment of one run as read now (ADR-0071). <c>Reason</c> is the exact canonical text (LF may separate lines),
/// <c>AbandonedAtUtc</c> a UTC round-trip string with seven fractional digits and <c>EventSequence</c> the canonical positive decimal
/// Int64 of the one supporting run.abandoned event, a string so no JavaScript number can round it. It proves no cancellation, workspace
/// repair, ownership release, current intake permission or publication.
/// </summary>
public sealed record RunAbandonmentInspectionRecordedResponse(
    Guid ProjectId, string Reason, string AbandonedAtUtc, Guid EventId, string EventSequence);
