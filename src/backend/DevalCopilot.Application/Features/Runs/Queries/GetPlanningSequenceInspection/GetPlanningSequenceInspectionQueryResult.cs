using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;

/// <summary>A non-Recorded status carries a null header and an empty list, never a partial count or list.</summary>
public sealed record GetPlanningSequenceInspectionQueryResult(
    Guid RunId,
    PlanningSequenceInspectionStatus Status,
    PlanningSequenceInspectionHeader? Sequence,
    IReadOnlyList<PlanningSequenceInspectionStep> Steps);
