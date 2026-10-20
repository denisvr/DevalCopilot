namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>One fresh reading: the closed status and the exact workspace it judged (null only when the project has none or the latest one
/// could not be read).</summary>
public sealed record WorkspaceExecutionAssessment(WorkspaceExecutionStatus Status, Guid? WorkspaceId);
