using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;

/// <summary>A non-Recorded status carries a null header and an empty list, never a partial count or list.</summary>
public sealed record GetLocalExecutionSequenceInspectionQueryResult(
    Guid RunId,
    LocalExecutionSequenceInspectionStatus Status,
    LocalExecutionInspectionHeader? Sequence,
    IReadOnlyList<LocalExecutionInspectionStep> Steps);
