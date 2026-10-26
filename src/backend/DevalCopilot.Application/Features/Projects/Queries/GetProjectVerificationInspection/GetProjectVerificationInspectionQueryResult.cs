namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;

/// <summary>Recorded carries the execution; Unavailable carries none.</summary>
public sealed record GetProjectVerificationInspectionQueryResult(
    Guid ProjectId,
    Guid ExecutionId,
    ProjectVerificationInspectionStatus Status,
    ProjectVerificationInspectionExecution? Execution);
