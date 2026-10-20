using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

namespace DevalCopilot.Application.Features.Projects.Queries.GetWorkspaceExecutionStatus;

/// <summary>The closed reading and the exact latest workspace it judged (null only for NoWorkspace or an unreadable latest workspace). The
/// reason code is the fixed code of the status, absent only for Idle.</summary>
public sealed record GetWorkspaceExecutionStatusQueryResult(
    Guid ProjectId, Guid? WorkspaceId, WorkspaceExecutionStatus Status, string? ReasonCode);
