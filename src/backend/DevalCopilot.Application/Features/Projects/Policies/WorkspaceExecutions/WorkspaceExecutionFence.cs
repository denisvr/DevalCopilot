using Devalente.Shared.Results;
using DevalCopilot.Application.Data;

namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>
/// The refusal every execution entrance shares (ADR-0057): a claim may be admitted only into an Idle workspace, and a single-use dispatch
/// gate only when no owner other than the exact operation being dispatched exists. Each check is a fresh, untracked read. A claim runs it
/// once before its expensive preparation and again after taking the existing short write lock, immediately before its commit; a dispatch
/// gate runs it under its own write lock. It terminalizes nothing, cancels nothing and chooses no winner.
/// </summary>
internal static class WorkspaceExecutionFence
{
    /// <summary>Refuses a claim into a workspace that is not Idle: another owner, a reservation, conflicting or untrustworthy ownership.</summary>
    public static async Task<Error?> CheckClaimAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid workspaceId, CancellationToken cancellationToken) =>
        ToError(await WorkspaceExecutionPolicy.AssessAsync(dbContext, projectId, workspaceId, null, null, cancellationToken));

    /// <summary>Refuses dispatching exactly this Agent attempt when anything else owns the workspace. Other attempts of its run and
    /// verification executions are never excluded.</summary>
    public static async Task<Error?> CheckAgentDispatchAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid workspaceId, Guid attemptId, CancellationToken cancellationToken) =>
        ToError(await WorkspaceExecutionPolicy.AssessAsync(dbContext, projectId, workspaceId, attemptId, null, cancellationToken));

    /// <summary>Refuses dispatching exactly this verification execution when anything else owns the workspace. An Agent attempt with the
    /// same identifier is never excluded.</summary>
    public static async Task<Error?> CheckVerificationDispatchAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid workspaceId, Guid executionId, CancellationToken cancellationToken) =>
        ToError(await WorkspaceExecutionPolicy.AssessAsync(dbContext, projectId, workspaceId, null, executionId, cancellationToken));

    /// <summary>The stable refusal for one reading, or null when it is Idle.</summary>
    public static Error? ToError(WorkspaceExecutionAssessment assessment) => assessment.Status switch
    {
        WorkspaceExecutionStatus.Idle => null,
        WorkspaceExecutionStatus.AgentAttemptActive => WorkspaceExecutionErrors.AgentAttemptActive(),
        WorkspaceExecutionStatus.VerificationActive => WorkspaceExecutionErrors.VerificationActive(),
        WorkspaceExecutionStatus.CommitReserved => CommitReservation.WorkspaceCommitting(),
        WorkspaceExecutionStatus.Conflicted => WorkspaceExecutionErrors.OwnersConflicted(),
        _ => WorkspaceExecutionErrors.Unavailable(),
    };
}
