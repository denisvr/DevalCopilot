using DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;

namespace DevalCopilot.Api.Features.LocalExecutionQueue.GetLocalExecutionQueueInspection;

/// <summary>
/// One outstanding recorded admission (ADR-0064): the registered project's identity and descriptive name (untrusted text, not pinned
/// authority), the run, the stored sequence bookkeeping and the admission, which is the proved last admission of that sequence and is
/// Waiting, Reserved or Unresolved. The sequence and admission have exactly the shapes of the ADR-0063 header and row. A recorded
/// reservation is bookkeeping, never a live process or ownership proof, and no operation, token, path or digest is carried.
/// </summary>
public sealed record LocalExecutionQueueInspectionEntryResponse(
    Guid ProjectId,
    string ProjectName,
    Guid RunId,
    LocalExecutionAdmissionHistoryHeaderResponse Sequence,
    LocalExecutionAdmissionHistoryAdmissionResponse Admission);
