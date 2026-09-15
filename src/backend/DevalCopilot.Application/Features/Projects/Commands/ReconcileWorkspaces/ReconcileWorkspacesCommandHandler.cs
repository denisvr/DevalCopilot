using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;

/// <summary>
/// Evidence-driven startup reconciliation for every <see cref="RepositoryMutationLease"/> still
/// <see cref="LeaseStatus.Active"/> from a previous host instance. Every transition below is
/// justified by direct, freshly observed evidence (Git's own worktree registration, the
/// ownership marker's typed fields, and the workspace's own current HEAD) — never assumed from
/// elapsed time or absence of a heartbeat, since neither exists in this slice. Promotion to
/// <see cref="WorkspaceStatus.Ready"/> only ever happens when every piece of evidence agrees;
/// any inconsistency, however small, fails closed instead.
///
/// <para>
/// Declared <see cref="IManualTransactionCommand{TResult}"/>: each lease's own transition (if
/// any) is committed via its own explicit, independent <see cref="IDevalCopilotDbContext.SaveChangesAsync"/>
/// immediately after that lease's evidence is decided — never inside an ambient transaction that
/// would otherwise span the external Git/marker I/O for every lease in the pass, and never one
/// that could let a later lease's failure roll back an earlier, already-decided lease's
/// transition. A lease found unchanged is never written at all. See ADR-0008.
/// </para>
/// </summary>
public sealed class ReconcileWorkspacesCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorktreeAdapter gitWorktreeAdapter,
    IWorkspaceOwnershipMarkerStore markerStore,
    TimeProvider timeProvider)
    : ICommandHandler<ReconcileWorkspacesCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(ReconcileWorkspacesCommand command, CancellationToken cancellationToken)
    {
        var activeLeases = await dbContext.RepositoryMutationLeases
            .Where(lease => lease.Status == LeaseStatus.Active)
            .ToListAsync(cancellationToken);

        var reconciledCount = 0;
        foreach (var lease in activeLeases)
        {
            var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == lease.WorkspaceId, cancellationToken);
            var project = await dbContext.Projects.SingleAsync(p => p.Id == workspace.ProjectId, cancellationToken);

            var changed = await ReconcileOneAsync(project, workspace, lease, cancellationToken);
            if (!changed)
            {
                // Nothing to persist for this lease — never written, exactly as found.
                continue;
            }

            // Committed immediately, independently of every other lease in this pass: no EF
            // transaction was open during the external I/O above, and this commit's success or
            // failure has no bearing on any other lease's already-committed (or yet-to-be-
            // decided) transition.
            await dbContext.SaveChangesAsync(cancellationToken);
            reconciledCount++;
        }

        return Result<int>.Success(reconciledCount);
    }

    private async Task<bool> ReconcileOneAsync(
        Project project, GitWorkspace workspace, RepositoryMutationLease lease, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        var registrationResult = await gitWorktreeAdapter.IsRegisteredAsync(
            project.CanonicalPath, workspace.WorkspacePath, cancellationToken);

        if (registrationResult.Outcome != GitWorktreeRegistrationOutcome.Registered)
        {
            // Not registered, or Git itself could not answer — either way, this worktree
            // cannot be trusted as it stands.
            return FailOrSupersede(
                workspace, lease, nowUtc, WorkspaceStatus.MissingExternally, "workspaces.reconciliation_worktree_missing");
        }

        var administrativeDirectoryResult = await gitWorktreeAdapter.ResolveAdministrativeDirectoryAsync(
            project.CanonicalPath, workspace.WorkspacePath, cancellationToken);

        if (administrativeDirectoryResult.Outcome != GitWorktreeAdministrativeDirectoryOutcome.Resolved
            || administrativeDirectoryResult.AdministrativeDirectory is null)
        {
            return FailOrSupersede(
                workspace, lease, nowUtc, WorkspaceStatus.AlteredExternally, "workspaces.reconciliation_administrative_directory_unresolved");
        }

        var markerResult = await markerStore.ReadAsync(administrativeDirectoryResult.AdministrativeDirectory, cancellationToken);
        var markerValid = markerResult.Outcome == WorkspaceOwnershipMarkerReadOutcome.Valid
            && MarkerMatches(markerResult.Marker, workspace, lease);

        if (!markerValid)
        {
            return FailOrSupersede(
                workspace, lease, nowUtc, WorkspaceStatus.AlteredExternally, "workspaces.reconciliation_marker_invalid");
        }

        var headResult = await gitWorktreeAdapter.GetHeadCommitShaAsync(workspace.WorkspacePath, cancellationToken);
        var headMatches = headResult.Outcome == GitWorktreeHeadOutcome.Resolved
            && string.Equals(headResult.CommitSha, workspace.SourceCommitSha, StringComparison.Ordinal);

        if (workspace.Status == WorkspaceStatus.Preparing)
        {
            if (headMatches)
            {
                // Both independent pieces of evidence — Git's own registration and the marker
                // — already agree with the database's intent, so promoting is safe, not an
                // assumption.
                workspace.MarkReady();
                return true;
            }

            // Ambiguous mid-preparation evidence is never resolved by promoting: fail closed
            // exactly like any other unrecoverable Preparing workspace.
            workspace.MarkFailedToPrepare("workspaces.reconciliation_inconsistent_evidence");
            lease.Release(nowUtc);
            return true;
        }

        if (workspace.Status == WorkspaceStatus.Ready && !headMatches)
        {
            workspace.MarkNeedsAttention("workspaces.reconciliation_head_diverged");
            return true;
        }

        // Already NeedsAttention, or Ready with matching HEAD: nothing to change. This slice
        // never attempts to reverse an existing NeedsAttention flag back to Ready.
        return false;
    }

    private static bool MarkerMatches(WorkspaceOwnershipMarker? marker, GitWorkspace workspace, RepositoryMutationLease lease) =>
        marker is not null
        && marker.WorkspaceId == workspace.Id
        && marker.ProjectId == workspace.ProjectId
        && marker.LeaseId == lease.Id
        && marker.PhysicalVolumeSerialNumber == lease.PhysicalVolumeSerialNumber
        && string.Equals(marker.PhysicalFileIdHex, Convert.ToHexString(lease.PhysicalFileId), StringComparison.OrdinalIgnoreCase);

    private static bool FailOrSupersede(
        GitWorkspace workspace, RepositoryMutationLease lease, DateTimeOffset nowUtc, WorkspaceStatus establishedTargetStatus, string reasonCode)
    {
        if (workspace.Status == WorkspaceStatus.Preparing)
        {
            // A workspace that never reached Ready has nothing established yet to be "missing"
            // or "altered" from — both collapse to the same never-retried failure.
            workspace.MarkFailedToPrepare(reasonCode);
            lease.Release(nowUtc);
            return true;
        }

        if (workspace.Status is WorkspaceStatus.Ready or WorkspaceStatus.NeedsAttention)
        {
            if (establishedTargetStatus == WorkspaceStatus.MissingExternally)
            {
                workspace.MarkMissingExternally(reasonCode);
            }
            else
            {
                workspace.MarkAlteredExternally(reasonCode);
            }

            lease.Supersede(nowUtc);
            return true;
        }

        return false;
    }
}
