namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// The recorded index of one run's local execution sequence (ADR-0061). <c>Status</c> is <c>NotRecorded</c> (a known run with no
/// sequence), <c>Unavailable</c> (a present sequence whose structure cannot be proved or changed during the read) or <c>Recorded</c>.
/// Only <c>Recorded</c> carries the header and the complete step list; every other status carries a null header and an empty list.
/// </summary>
public sealed record GetLocalExecutionSequenceInspectionResponse(
    Guid RunId,
    string Status,
    LocalExecutionInspectionHeaderResponse? Sequence,
    IReadOnlyList<LocalExecutionInspectionStepResponse> Steps);
