namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;

/// <summary>
/// The recorded admission ledger of one run's queued local execution cycle (ADR-0063). <c>Status</c> is <c>NotRecorded</c> (a known run
/// with no sequence), <c>NotQueued</c> (a coherent Immediate sequence), <c>Unavailable</c> (a present root, structure or linkage that
/// cannot be proved) or <c>Recorded</c>. Only <c>Recorded</c> carries the header and the complete nonempty ledger in admission order;
/// every other status carries a null header and an empty list.
/// </summary>
public sealed record GetLocalExecutionAdmissionHistoryResponse(
    Guid RunId,
    string Status,
    LocalExecutionAdmissionHistoryHeaderResponse? Sequence,
    IReadOnlyList<LocalExecutionAdmissionHistoryAdmissionResponse> Admissions);
