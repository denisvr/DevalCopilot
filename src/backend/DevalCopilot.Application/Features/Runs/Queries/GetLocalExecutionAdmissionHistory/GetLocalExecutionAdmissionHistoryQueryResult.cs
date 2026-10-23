using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;

/// <summary>A non-Recorded status carries a null header and an empty list, never a partial count or list.</summary>
public sealed record GetLocalExecutionAdmissionHistoryQueryResult(
    Guid RunId,
    LocalExecutionAdmissionHistoryStatus Status,
    LocalExecutionAdmissionHistoryHeader? Sequence,
    IReadOnlyList<LocalExecutionInspectionAdmission> Admissions);
