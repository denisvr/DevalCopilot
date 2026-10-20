using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

/// <summary>
/// The fresh authority both pre-launch decisions (the single-use dispatch marker and the pre-dispatch SourceChanged recording)
/// share, so they can never disagree about who may act on an undispatched execution. Every read is untracked and is made inside
/// the caller's write-locked transaction: a tracked entity retained by the context is never authority.
/// </summary>
internal static class VerificationDispatchAuthority
{
    public static Task<VerificationExecution?> ReadExecutionAsync(
        IDevalCopilotDbContext dbContext, Guid executionId, CancellationToken cancellationToken) =>
        dbContext.VerificationExecutions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == executionId, cancellationToken);

    /// <summary>The first failed fact, or <see langword="null"/> when the durable execution agrees with the expected snapshot and
    /// still belongs to its project's latest workspace, which is ready, owned by an active lease, at the path the execution
    /// recorded, with the execution's checkpoint still on it under the same fingerprint.</summary>
    public static async Task<Error?> ConfirmAsync(
        IDevalCopilotDbContext dbContext,
        VerificationExecution execution,
        VerificationDispatchSnapshot expected,
        CancellationToken cancellationToken)
    {
        if (!VerificationDispatchSnapshot.Of(execution).Equals(expected))
        {
            return Error.Conflict(
                "verification.execution_snapshot_changed", "This verification execution no longer matches the request prepared for it.");
        }

        var latest = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == execution.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .Select(candidate => new { candidate.Id, candidate.Status, candidate.WorkspacePath })
            .FirstOrDefaultAsync(cancellationToken);
        var checkpointCurrent = await dbContext.GitCheckpoints.AsNoTracking().AnyAsync(
            candidate => candidate.Id == execution.GitCheckpointId
                && candidate.WorkspaceId == execution.GitWorkspaceId
                && candidate.FingerprintSha256 == execution.CheckpointFingerprintSha256,
            cancellationToken);
        var leaseActive = await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
            lease => lease.WorkspaceId == execution.GitWorkspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!(latest is not null
            && latest.Id == execution.GitWorkspaceId
            && latest.Status == WorkspaceStatus.Ready
            && string.Equals(latest.WorkspacePath, execution.WorkspacePath, StringComparison.Ordinal)
            && leaseActive
            && checkpointCurrent))
        {
            return Error.Conflict("verification.execution_not_current", "This verification execution is no longer eligible to run.");
        }

        // ADR-0057: the one dispatch authority of both pre-launch decisions also requires that nothing but this exact execution owns its
        // workspace (another Agent attempt, a second execution, a local-commit reservation or untrustworthy ownership refuses both).
        return await WorkspaceExecutionFence.CheckVerificationDispatchAsync(
            dbContext, execution.ProjectId, execution.GitWorkspaceId, execution.Id, cancellationToken);
    }
}
