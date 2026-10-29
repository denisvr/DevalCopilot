namespace DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;

/// <summary><see cref="NotRecorded"/>: a recognized non-Abandoned run with no recorded abandonment fact in this read; it says nothing
/// about eligibility, ownership or another objective. <see cref="Unavailable"/>: recorded abandonment markers or events cannot pass the
/// selected proof (missing, stray, damaged, unsupported or duplicate evidence). <see cref="Recorded"/>: one complete abandonment passed
/// the bounded proof.</summary>
public enum RunAbandonmentInspectionStatus
{
    NotRecorded,
    Unavailable,
    Recorded,
}
