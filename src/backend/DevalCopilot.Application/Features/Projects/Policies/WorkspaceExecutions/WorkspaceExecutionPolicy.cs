using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>
/// The one fresh, untracked reading of who may execute in a project's owned workspace (ADR-0057). Ownership is derived only from rows
/// that already exist: an unresolved Agent attempt of the workspace, an unresolved verification execution of the workspace, and ADR-0029's
/// reservation. It never reads Git, a provider, a process table or AgentSupervision, and it adds no table, lease or counter.
///
/// <para>
/// Every predicate is a database-side exclusion of the recognized terminal set, so a stored value this build does not recognize is never
/// materialized, never classified as finished and keeps the workspace owned. Counts are compared, not rows, so no identifier is read except
/// the latest workspace's. Two or more coherent owners are Conflicted without choosing a winner; an owner whose association cannot be
/// trusted (an Agent attempt of the project that names no workspace or a workspace of another project, a verification execution or an open
/// local commit whose project differs from its workspace's, checked from both sides: another project's row naming this workspace and this
/// project's row naming another project's workspace, a reservation without a Ready-or-Committing workspace) makes the reading
/// Unavailable. An older workspace of the same project is valid and never untrusted. A terminal owner never blocks by
/// its status, and terminal status is never a proof that its process ended.
/// </para>
///
/// <para>
/// A caller that is itself the owner under judgment (a dispatch gate) excludes exactly that Agent attempt or that verification execution,
/// never its run, its contract or an identifier of the other table.
/// </para>
/// </summary>
internal static class WorkspaceExecutionPolicy
{
    /// <summary>The project's latest workspace (by number) and its reading, for the protected status. An unreadable latest identifier is
    /// Unavailable with no workspace, never a guess.</summary>
    public static async Task<WorkspaceExecutionAssessment> AssessLatestAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken)
    {
        Guid? latest;
        try
        {
            latest = await dbContext.GitWorkspaces
                .AsNoTracking()
                .Where(candidate => candidate.ProjectId == projectId)
                .OrderByDescending(candidate => candidate.WorkspaceNumber)
                .Select(candidate => (Guid?)candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Unavailable, null);
        }

        return latest is { } workspaceId
            ? await AssessAsync(dbContext, projectId, workspaceId, null, null, cancellationToken)
            : new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.NoWorkspace, null);
    }

    /// <summary>The reading of one named workspace of the project, optionally ignoring exactly one Agent attempt or one verification
    /// execution (the caller's own).</summary>
    public static async Task<WorkspaceExecutionAssessment> AssessAsync(
        IDevalCopilotDbContext dbContext,
        Guid projectId,
        Guid workspaceId,
        Guid? excludedAgentAttemptId,
        Guid? excludedVerificationExecutionId,
        CancellationToken cancellationToken)
    {
        var workspace = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(candidate => candidate.Id == workspaceId)
            .Select(candidate => new
            {
                BelongsToProject = candidate.ProjectId == projectId,
                IsReady = candidate.Status == WorkspaceStatus.Ready,
                IsCommitting = candidate.Status == WorkspaceStatus.Committing,
                IsNeedsAttention = candidate.Status == WorkspaceStatus.NeedsAttention,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null || !workspace.BelongsToProject)
        {
            return Unavailable(workspaceId);
        }

        var projectRunIds = dbContext.Runs.AsNoTracking().Where(run => run.ProjectId == projectId).Select(run => run.Id);
        var attempts = dbContext.Attempts.AsNoTracking().Where(attempt => attempt.Kind == AttemptKind.Agent
            && attempt.Status != AttemptStatus.Completed
            && attempt.Status != AttemptStatus.Failed
            && attempt.Status != AttemptStatus.Interrupted);
        if (excludedAgentAttemptId is { } ownAttemptId)
        {
            attempts = attempts.Where(attempt => attempt.Id != ownAttemptId);
        }

        var agentOwners = await attempts.CountAsync(
            attempt => attempt.AgentGitWorkspaceId == workspaceId && projectRunIds.Contains(attempt.RunId), cancellationToken);
        var agentUntrusted = await attempts.CountAsync(
            attempt => (attempt.AgentGitWorkspaceId == workspaceId && !projectRunIds.Contains(attempt.RunId))
                || (projectRunIds.Contains(attempt.RunId)
                    && (attempt.AgentGitWorkspaceId == null
                        || !dbContext.GitWorkspaces.Any(owned => owned.Id == attempt.AgentGitWorkspaceId && owned.ProjectId == projectId))),
            cancellationToken);

        var executions = dbContext.VerificationExecutions.AsNoTracking().Where(execution =>
            execution.Status != VerificationExecutionStatus.Passed
            && execution.Status != VerificationExecutionStatus.Failed
            && execution.Status != VerificationExecutionStatus.TimedOut
            && execution.Status != VerificationExecutionStatus.Cancelled
            && execution.Status != VerificationExecutionStatus.Interrupted
            && execution.Status != VerificationExecutionStatus.SourceChanged);
        if (excludedVerificationExecutionId is { } ownExecutionId)
        {
            executions = executions.Where(execution => execution.Id != ownExecutionId);
        }

        var verificationOwners = await executions.CountAsync(
            execution => execution.GitWorkspaceId == workspaceId && execution.ProjectId == projectId, cancellationToken);
        var verificationUntrusted = await executions.CountAsync(
            execution => (execution.GitWorkspaceId == workspaceId && execution.ProjectId != projectId)
                || (execution.ProjectId == projectId
                    && !dbContext.GitWorkspaces.Any(owned => owned.Id == execution.GitWorkspaceId && owned.ProjectId == projectId)),
            cancellationToken);

        var openOperations = dbContext.LocalCommitOperations.AsNoTracking().Where(operation =>
            operation.Status != LocalCommitStatus.Completed
            && operation.Status != LocalCommitStatus.Failed
            && operation.Status != LocalCommitStatus.Interrupted);
        var coherentOpenOperations = await openOperations.CountAsync(
            operation => operation.GitWorkspaceId == workspaceId && operation.ProjectId == projectId, cancellationToken);
        var untrustedOpenOperations = await openOperations.CountAsync(
            operation => (operation.GitWorkspaceId == workspaceId && operation.ProjectId != projectId)
                || (operation.ProjectId == projectId
                    && !dbContext.GitWorkspaces.Any(owned => owned.Id == operation.GitWorkspaceId && owned.ProjectId == projectId)),
            cancellationToken);

        // ADR-0029's reservation, exactly as CommitReservation reads it: a Committing workspace, or a NeedsAttention one while its own
        // nonterminal operation is open. A nonterminal operation of a workspace that is neither is not a reservation to trust.
        var commitReserved = workspace.IsCommitting || (workspace.IsNeedsAttention && coherentOpenOperations > 0);
        var commitUntrusted = untrustedOpenOperations > 0 || (coherentOpenOperations > 0 && !commitReserved);

        var owners = agentOwners + verificationOwners + (commitReserved ? 1 : 0);
        if (owners > 1)
        {
            return new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Conflicted, workspaceId);
        }

        if (agentUntrusted > 0 || verificationUntrusted > 0 || commitUntrusted)
        {
            return Unavailable(workspaceId);
        }

        if (agentOwners == 1)
        {
            return new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.AgentAttemptActive, workspaceId);
        }

        if (verificationOwners == 1)
        {
            return new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.VerificationActive, workspaceId);
        }

        if (commitReserved)
        {
            return new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.CommitReserved, workspaceId);
        }

        if (!workspace.IsReady)
        {
            return Unavailable(workspaceId);
        }

        var leaseActive = await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
            lease => lease.WorkspaceId == workspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        return leaseActive
            ? new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Idle, workspaceId)
            : Unavailable(workspaceId);
    }

    private static WorkspaceExecutionAssessment Unavailable(Guid workspaceId) =>
        new(WorkspaceExecutionStatus.Unavailable, workspaceId);
}
