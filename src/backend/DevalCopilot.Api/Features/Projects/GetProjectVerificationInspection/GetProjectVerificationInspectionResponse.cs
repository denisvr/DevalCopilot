namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationInspection;

/// <summary>The recorded project verification selected by its exact identity (ADR-0069). <c>Status</c> is <c>Recorded</c> (with the
/// execution) or <c>Unavailable</c> (the execution belongs to the project but its recorded facts are incoherent; a null execution).</summary>
public sealed record GetProjectVerificationInspectionResponse(
    Guid ProjectId, Guid ExecutionId, string Status, ProjectVerificationInspectionExecutionResponse? Execution);
