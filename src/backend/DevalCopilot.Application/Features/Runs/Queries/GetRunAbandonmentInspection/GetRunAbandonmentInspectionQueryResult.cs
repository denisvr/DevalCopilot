using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;

/// <summary>Recorded carries the abandonment; NotRecorded and Unavailable carry none.</summary>
public sealed record GetRunAbandonmentInspectionQueryResult(
    Guid RunId, RunAbandonmentInspectionStatus Status, RecordedAbandonment? Abandonment);
