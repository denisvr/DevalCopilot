using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The early, readable refusal for writes that must not race an explicit local commit (ADR-0029). The reservation is the whole
/// admitted, nonterminal operation: its workspace is Committing, or NeedsAttention while that exact operation (same workspace and
/// project) is still open because an outcome was ambiguous. A terminal operation, a workspace flagged for any other reason and
/// every other project are never reserved. The decisive exclusion is the database guard of the same predicate (migration
/// <c>AddLocalCommitAttentionExclusion</c>), which fires inside the competing writer's own write transaction; this check only
/// gives a clear conflict before that point.
/// </summary>
public static class CommitReservation
{
    public const string WorkspaceCommittingCode = "workspaces.committing";

    public static Error WorkspaceCommitting() => Error.Conflict(
        WorkspaceCommittingCode,
        "A local commit holds this project's workspace until it completes or its outcome is resolved; wait for it to finish.");

    /// <summary>Saves the pending writes. When the database guard refuses them because a local commit reserved the project's
    /// workspace after this writer's own Ready read, the stable conflict is returned and nothing is persisted; any other
    /// persistence failure is not ours to reinterpret and propagates.</summary>
    public static async Task<Error?> TrySaveAsync(IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            if (!await IsProjectReservedAsync(dbContext, projectId, CancellationToken.None))
            {
                throw;
            }

            return WorkspaceCommitting();
        }
    }

    public static Task<bool> IsProjectReservedAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken) =>
        dbContext.GitWorkspaces.AsNoTracking().AnyAsync(
            workspace => workspace.ProjectId == projectId
                && (workspace.Status == WorkspaceStatus.Committing
                    || (workspace.Status == WorkspaceStatus.NeedsAttention
                        && dbContext.LocalCommitOperations.Any(operation =>
                            operation.GitWorkspaceId == workspace.Id
                            && operation.ProjectId == workspace.ProjectId
                            && operation.Status != LocalCommitStatus.Completed
                            && operation.Status != LocalCommitStatus.Failed
                            && operation.Status != LocalCommitStatus.Interrupted))),
            cancellationToken);
}
