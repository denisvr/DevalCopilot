using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.Abandonment;

/// <summary>
/// The fresh, untracked check that nothing of a project is active or ambiguous (ADR-0031). It is shared by the command, which calls it
/// under the already-taken SQLite write lock and decides from it, and by the read-only status, whose answer is advisory only.
///
/// <para>
/// Every predicate is a database-side exclusion of the recognized terminal set, so a stored value this build does not recognize is
/// never materialized, never classified as finished and blocks. Attempts and verification executions are scoped to the whole project,
/// and so is every local-commit operation, because the workspace and its single mutating writer are project-owned. A missing workspace
/// is valid; a workspace left in any other recognized state is retained as it is, since abandonment changes no workspace, lease,
/// marker or source trust.
/// </para>
/// </summary>
internal static class RunAbandonmentAuthority
{
    /// <summary>The first blocking fact in a fixed order, or <see langword="null"/> when the project has none.</summary>
    public static async Task<Error?> FindBlockerAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken)
    {
        var projectRunIds = dbContext.Runs.AsNoTracking().Where(run => run.ProjectId == projectId).Select(run => run.Id);

        if (await dbContext.Attempts.AsNoTracking().Where(LocalExecutionOwnership.UnresolvedAttempt(dbContext)).AnyAsync(
                attempt => projectRunIds.Contains(attempt.RunId),
                cancellationToken))
        {
            return RunAbandonmentErrors.ActiveAttempt();
        }

        if (await dbContext.VerificationExecutions.AsNoTracking().Where(LocalExecutionOwnership.UnresolvedExecution(dbContext)).AnyAsync(
                execution => execution.ProjectId == projectId,
                cancellationToken))
        {
            return RunAbandonmentErrors.ActiveVerification();
        }

        // Prepared, Executing and NeedsAttention are all open: an ambiguous local delivery is never overridden by abandoning.
        if (await dbContext.LocalCommitOperations.AsNoTracking().AnyAsync(
                operation => operation.ProjectId == projectId
                    && operation.Status != LocalCommitStatus.Completed
                    && operation.Status != LocalCommitStatus.Failed
                    && operation.Status != LocalCommitStatus.Interrupted,
                cancellationToken))
        {
            return RunAbandonmentErrors.LocalCommitOpen();
        }

        if (await dbContext.GitWorkspaces.AsNoTracking().AnyAsync(
                workspace => workspace.ProjectId == projectId
                    && workspace.Status != WorkspaceStatus.Ready
                    && workspace.Status != WorkspaceStatus.FailedToPrepare
                    && workspace.Status != WorkspaceStatus.MissingExternally
                    && workspace.Status != WorkspaceStatus.AlteredExternally
                    && workspace.Status != WorkspaceStatus.NeedsAttention,
                cancellationToken))
        {
            return RunAbandonmentErrors.WorkspaceBusy();
        }

        // ADR-0062: a held queued step preparation is a workspace being prepared; it is never released by abandoning.
        if (await LocalExecutionReservationOwnership.Held(dbContext).AnyAsync(
                admission => dbContext.LocalExecutionSequences.Any(
                    sequence => sequence.Id == admission.SequenceId && sequence.ProjectId == projectId),
                cancellationToken))
        {
            return RunAbandonmentErrors.WorkspaceBusy();
        }

        return null;
    }
}
