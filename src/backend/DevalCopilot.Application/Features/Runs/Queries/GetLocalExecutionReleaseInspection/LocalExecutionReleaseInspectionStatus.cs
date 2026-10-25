namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;

/// <summary>The closed status of one recorded release inspection (ADR-0067). Only Recorded carries a receipt. NotRecorded means only that
/// this read proved no receipt for the step; it says nothing about quarantine, termination, eligibility or ownership. Unavailable is any
/// unreadable, incomplete, ambiguous, unsupported or invalid evidence, including a present receipt that fails a proof.</summary>
public enum LocalExecutionReleaseInspectionStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    Recorded = 2,
}
