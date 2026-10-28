using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;

/// <summary>Recorded carries the operation; NotRecorded and Unavailable carry none.</summary>
public sealed record GetLocalCommitInspectionQueryResult(
    Guid RunId, LocalCommitInspectionStatus Status, RecordedLocalCommitOperation? Operation);
