namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionReleaseInspection;

/// <summary>
/// The closed graph of one step's recorded release inspection (ADR-0067). <c>Status</c> is exactly <c>Recorded</c>, <c>NotRecorded</c>
/// or <c>Unavailable</c>; <c>Receipt</c> is present only for <c>Recorded</c>. NotRecorded means only that this read proved no receipt for
/// the step and Unavailable that the evidence could not be proved; neither says whether the operation's ownership is held or released.
/// </summary>
public sealed record GetLocalExecutionReleaseInspectionResponse(
    Guid RunId,
    Guid StepId,
    string Status,
    LocalExecutionReleaseInspectionReceiptResponse? Receipt);
