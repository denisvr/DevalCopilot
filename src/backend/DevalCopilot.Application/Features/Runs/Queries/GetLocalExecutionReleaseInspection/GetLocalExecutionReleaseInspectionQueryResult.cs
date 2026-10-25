namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;

/// <summary>The closed graph of one recorded release inspection (ADR-0067). <see cref="Receipt"/> is set exactly for Recorded.</summary>
public sealed record GetLocalExecutionReleaseInspectionQueryResult(
    Guid RunId,
    Guid StepId,
    LocalExecutionReleaseInspectionStatus Status,
    LocalExecutionReleaseInspectionReceipt? Receipt);
