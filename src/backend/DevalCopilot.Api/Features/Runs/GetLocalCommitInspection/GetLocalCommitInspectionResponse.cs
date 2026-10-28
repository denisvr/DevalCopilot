namespace DevalCopilot.Api.Features.Runs.GetLocalCommitInspection;

/// <summary>
/// The closed graph of one run's recorded local-commit inspection (ADR-0070). <c>Status</c> is exactly <c>Recorded</c>,
/// <c>NotRecorded</c> or <c>Unavailable</c>; <c>Operation</c> is present only for <c>Recorded</c>. NotRecorded means only that no operation
/// is associated with this exact run in this read (it says nothing about eligibility or a previous request) and Unavailable that an
/// associated operation exists whose facts could not be proved.
/// </summary>
public sealed record GetLocalCommitInspectionResponse(Guid RunId, string Status, LocalCommitInspectionOperationResponse? Operation);
