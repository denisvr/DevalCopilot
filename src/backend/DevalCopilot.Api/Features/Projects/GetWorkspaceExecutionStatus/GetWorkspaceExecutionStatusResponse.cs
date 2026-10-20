namespace DevalCopilot.Api.Features.Projects.GetWorkspaceExecutionStatus;

/// <summary>
/// Who may execute in the project's latest owned workspace, as read now from existing durable rows (ADR-0057), and nothing else: no path,
/// payload, transcript, owner collection or inferred liveness. <c>Status</c> is one of NoWorkspace, Idle, AgentAttemptActive,
/// VerificationActive, CommitReserved, Conflicted and Unavailable, with exactly these tuples. NoWorkspace has no <c>WorkspaceId</c> and
/// reason <c>workspace_not_prepared</c>; Idle has the exact latest workspace and no reason; AgentAttemptActive, VerificationActive,
/// CommitReserved and Conflicted have it with <c>agent_attempt_active</c>, <c>verification_active</c>, <c>local_commit_reserved</c> and
/// <c>conflicting_execution_owners</c>; Unavailable has the exact latest workspace when it could be read, otherwise none, with
/// <c>workspace_execution_unavailable</c>. Idle states only what this read found; it is never permission, provider readiness or current
/// source attestation.
/// </summary>
public sealed record GetWorkspaceExecutionStatusResponse(Guid ProjectId, Guid? WorkspaceId, string Status, string? ReasonCode);
