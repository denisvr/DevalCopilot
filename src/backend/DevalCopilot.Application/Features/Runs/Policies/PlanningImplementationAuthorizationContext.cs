using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The fresh execution context both the authorization command and the authorized implementation claim confirm inside
/// their own write-locked transaction (ADR-0016). The first statement is an atomic update that requires an
/// Agent-admitting stored execution mode and thereby takes the database write lock; every later read is untracked, so a
/// commit by another connection before the transaction began is seen and a tracked entity loaded earlier confers no
/// authority. It is only called inside an open transaction, after every external step.
/// </summary>
internal static class PlanningImplementationAuthorizationContext
{
    internal enum Failure
    {
        ModeNotAdmitted,
        NotRunning,
        NotCurrent,
    }

    /// <summary>The first failed fact, or <see langword="null"/> when the run is running, admits Agent work, and the
    /// given workspace is ready with an active lease whose current checkpoint and fingerprint are exactly the given ones.</summary>
    public static async Task<Failure?> ConfirmAsync(
        IDevalCopilotDbContext dbContext,
        Run run,
        Guid workspaceId,
        Guid checkpointId,
        string checkpointFingerprintSha256,
        CancellationToken cancellationToken)
    {
        if (!await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, cancellationToken))
        {
            return Failure.ModeNotAdmitted;
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(
                candidate => candidate.Id == run.Id && candidate.Lifecycle == RunLifecycle.Running, cancellationToken))
        {
            return Failure.NotRunning;
        }

        var workspaceReady = await dbContext.GitWorkspaces.AsNoTracking().AnyAsync(
            candidate => candidate.Id == workspaceId && candidate.ProjectId == run.ProjectId && candidate.Status == WorkspaceStatus.Ready,
            cancellationToken);
        var leaseActive = await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
            lease => lease.WorkspaceId == workspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        var current = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspaceId)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => new { candidate.Id, candidate.FingerprintSha256 })
            .FirstOrDefaultAsync(cancellationToken);
        return workspaceReady
            && leaseActive
            && current is not null
            && current.Id == checkpointId
            && string.Equals(current.FingerprintSha256, checkpointFingerprintSha256, StringComparison.Ordinal)
                ? null
                : Failure.NotCurrent;
    }
}
