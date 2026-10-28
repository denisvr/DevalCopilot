namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;

/// <summary><see cref="NotRecorded"/>: no operation is associated with that exact run in this read; it says nothing about eligibility or
/// a previous request. <see cref="Unavailable"/>: an associated operation exists but its selected facts cannot be admitted, ownership
/// cannot be proved or more than one associated row exists. <see cref="Recorded"/>: one complete operation passed the bounded proof.</summary>
public enum LocalCommitInspectionStatus
{
    NotRecorded,
    Unavailable,
    Recorded,
}
