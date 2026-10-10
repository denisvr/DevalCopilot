namespace DevalCopilot.Api.Features.Projects.GetProjectRunHistory;

/// <param name="Lifecycle">A known lifecycle name, or the fixed value <c>Unrecognized</c>; a stored value is never exposed.</param>
/// <param name="Stage">A known stage name, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="ExecutionMode">Legacy, Simulated, ManualAgent, or Unrecognized.</param>
/// <param name="ReceiptSource">Present only for a run recorded as Completed whose own recorded Completed local commit can be
/// identified; it locates a receipt and does not certify one. Null says only that no source is available here.</param>
public sealed record ProjectRunHistoryEntryResponse(
    Guid ProjectId,
    Guid RunId,
    int ExecutionNumber,
    string Objective,
    string Lifecycle,
    string Stage,
    string ExecutionMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastAdvancedAtUtc,
    ProjectRunHistoryReceiptSourceResponse? ReceiptSource);
