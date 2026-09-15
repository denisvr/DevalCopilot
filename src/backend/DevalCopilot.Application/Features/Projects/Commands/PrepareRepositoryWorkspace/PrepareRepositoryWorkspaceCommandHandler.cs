using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;

/// <summary>
/// Orchestrates the full workspace-preparation sequence in one handler, across two independent,
/// immediately committed transactions rather than one ambient transaction spanning the whole
/// method — possible only because <see cref="PrepareRepositoryWorkspaceCommand"/> is declared
/// <see cref="IManualTransactionCommand{TResult}"/>, so <c>AddDevalenteEfCoreTransactions</c>
/// never opens its own transaction around this handler. The first
/// <see cref="IDevalCopilotDbContext.SaveChangesAsync"/> call durably commits a
/// <see cref="RepositoryMutationLease"/> and a <see cref="GitWorkspace"/> in
/// <see cref="WorkspaceStatus.Preparing"/> — genuinely committed to disk at that point, not
/// merely staged inside a still-open transaction — before any external Git or filesystem side
/// effect is attempted; the database's <c>Active</c>-only partial unique index on physical
/// identity is what rejects a concurrently committed second lease there. Everything between the
/// two <c>SaveChangesAsync</c> calls runs with no EF transaction open at all. If an exception or
/// cancellation propagates from that external I/O, this method returns without ever reaching the
/// second call: the <c>Preparing</c>/<c>Active</c> state committed by the first call is left
/// exactly as it stood — a truthful, evidence-matching starting point for
/// <c>ReconcileWorkspacesCommandHandler</c> at the next startup, never rolled back and never
/// papered over with an invented terminal state. See ADR-0008.
/// </summary>
public sealed class PrepareRepositoryWorkspaceCommandHandler(
    IDevalCopilotDbContext dbContext,
    IRepositoryRootPathInspector rootPathInspector,
    IRepositoryPhysicalIdentityInspector physicalIdentityInspector,
    IGitRepositoryInspector gitRepositoryInspector,
    IGitWorktreeAdapter gitWorktreeAdapter,
    IWorkspaceOwnershipMarkerStore markerStore,
    IWorkspaceRootPathProvider workspaceRootPathProvider,
    TimeProvider timeProvider)
    : ICommandHandler<PrepareRepositoryWorkspaceCommand, Result<PrepareRepositoryWorkspaceCommandResult>>
{
    public async Task<Result<PrepareRepositoryWorkspaceCommandResult>> HandleAsync(
        PrepareRepositoryWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return Fail(Error.NotFound("projects.not_found", "This project does not exist."));
        }

        // Bounded, one-shot recovery: never a loop. If identity is already Resolved this is a
        // no-op check further below re-verifies it fresh anyway.
        if (project.PhysicalIdentityStatus != PhysicalIdentityStatus.Resolved)
        {
            var recheckOutcome = PhysicalIdentityRecheck.Apply(project, rootPathInspector, physicalIdentityInspector);
            if (recheckOutcome.IsFailure)
            {
                return Fail(recheckOutcome.Errors[0]);
            }

            if (recheckOutcome.Value != PhysicalIdentityStatus.Resolved)
            {
                return Fail(MapUnavailableReason(project.PhysicalIdentityFailureReason));
            }
        }

        var gitSnapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.Capability == Capability.Git, cancellationToken);
        if (gitSnapshot is null || gitSnapshot.ReasonCode != CapabilityProbeReason.None)
        {
            return Fail(Error.Conflict("workspaces.git_unavailable", "Git is not currently available on this host."));
        }

        // Defense-in-depth: the database's partial unique index (exercised below) is the real,
        // race-free guarantee. This pre-check only avoids a wasted root/Git inspection in the
        // common, non-racing case.
        var alreadyActive = await dbContext.RepositoryMutationLeases.AnyAsync(
            lease => lease.ProjectId == project.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (alreadyActive)
        {
            return Fail(AlreadyActiveError());
        }

        var rootResult = rootPathInspector.Inspect(project.CanonicalPath);
        if (rootResult.Outcome != RepositoryRootInspectionOutcome.Success || rootResult.Candidate is null)
        {
            return Fail(MapRootOutcome(rootResult.Outcome));
        }

        var candidate = rootResult.Candidate;

        // The fresh pre-mutation physical-identity re-verification: re-resolved right now,
        // compared against the persisted tuple, regardless of how recently it was last
        // confirmed. Never overwrites the persisted identity itself — only this request blocks.
        var identityResult = physicalIdentityInspector.Resolve(candidate);
        if (identityResult.Outcome != RepositoryPhysicalIdentityInspectionOutcome.Resolved)
        {
            return Fail(Error.Conflict(
                "workspaces.physical_identity_check_failed", "This project's physical identity could not be verified right now."));
        }

        var volumeSerialNumber = identityResult.VolumeSerialNumber!.Value;
        var fileId = identityResult.FileId!;
        if (!project.PhysicalIdentityMatches(volumeSerialNumber, fileId))
        {
            return Fail(Error.Conflict(
                "workspaces.physical_identity_mismatch", "This path no longer matches its registered physical identity."));
        }

        var gitResult = await gitRepositoryInspector.InspectAsync(candidate, cancellationToken);
        if (gitResult.Outcome != GitRepositoryInspectionOutcome.Success)
        {
            return Fail(MapGitOutcome(gitResult.Outcome));
        }

        if (gitResult.HeadState == RepositoryHeadState.Unborn)
        {
            return Fail(Error.Conflict("workspaces.unborn_repository_not_supported", "This repository has no commits yet."));
        }

        if (gitResult.IsDirty)
        {
            return Fail(Error.Conflict(
                "workspaces.dirty_repository_not_supported", "This repository has uncommitted changes. Commit or discard them first."));
        }

        var sourceCommitSha = gitResult.HeadCommitSha!;
        var sourceBranchName = gitResult.BranchName;

        var workspaceNumber = project.ReserveWorkspaceNumber();
        var branchName = WorkspaceBranchNamePolicy.Compute(project.Id, workspaceNumber);
        var workspacePath = workspaceRootPathProvider.ComputeWorkspacePath(project.Id, workspaceNumber);

        var nowUtc = timeProvider.GetUtcNow();
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, workspaceNumber, workspacePath, branchName, sourceCommitSha, sourceBranchName, nowUtc);
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, volumeSerialNumber, fileId, nowUtc);

        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);

        try
        {
            // The durable-intent commit: from this point, a crash is recoverable by startup
            // reconciliation rather than silently lost. This explicit save is also the actual
            // exclusivity enforcement point — the partial unique index on
            // (PhysicalVolumeSerialNumber, PhysicalFileId) filtered to Active rows rejects a
            // concurrently committed second lease for this physical repository.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Fail(AlreadyActiveError());
        }

        // Everything below is external I/O, deliberately outside any further database
        // transaction until the compensating or final save.
        var creationResult = await gitWorktreeAdapter.CreateAsync(
            project.CanonicalPath, workspacePath, branchName, sourceCommitSha, cancellationToken);
        if (creationResult.Outcome != GitWorktreeCreationOutcome.Success)
        {
            return await FailPreparationAsync(workspace, lease, MapCreationOutcome(creationResult.Outcome), cancellationToken);
        }

        var administrativeDirectoryResult = await gitWorktreeAdapter.ResolveAdministrativeDirectoryAsync(
            project.CanonicalPath, workspacePath, cancellationToken);
        if (administrativeDirectoryResult.Outcome != GitWorktreeAdministrativeDirectoryOutcome.Resolved
            || administrativeDirectoryResult.AdministrativeDirectory is null)
        {
            return await FailPreparationAsync(
                workspace,
                lease,
                Error.Conflict(
                    "workspaces.administrative_directory_not_resolved",
                    "This workspace's Git administrative directory could not be verified."),
                cancellationToken);
        }

        var marker = new WorkspaceOwnershipMarker(
            workspace.Id, project.Id, lease.Id, volumeSerialNumber, Convert.ToHexString(fileId));
        var writeResult = await markerStore.WriteAsync(
            administrativeDirectoryResult.AdministrativeDirectory, marker, cancellationToken);
        if (writeResult.Outcome != WorkspaceOwnershipMarkerWriteOutcome.Success)
        {
            return await FailPreparationAsync(
                workspace,
                lease,
                Error.Conflict("workspaces.marker_write_failed", "This workspace's ownership marker could not be written."),
                cancellationToken);
        }

        workspace.MarkReady();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<PrepareRepositoryWorkspaceCommandResult>.Success(new PrepareRepositoryWorkspaceCommandResult(
            workspace.Id, workspace.WorkspacePath, workspace.BranchName, workspace.SourceCommitSha, workspace.SourceBranchName));
    }

    private async Task<Result<PrepareRepositoryWorkspaceCommandResult>> FailPreparationAsync(
        GitWorkspace workspace, RepositoryMutationLease lease, Error error, CancellationToken cancellationToken)
    {
        workspace.MarkFailedToPrepare(error.Code);
        lease.Release(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Fail(error);
    }

    private static Result<PrepareRepositoryWorkspaceCommandResult> Fail(Error error) =>
        Result<PrepareRepositoryWorkspaceCommandResult>.Failure(error);

    private static Error AlreadyActiveError() => Error.Conflict(
        "workspaces.already_has_active_workspace", "This repository already has an active candidate workspace.");

    private static Error MapUnavailableReason(PhysicalIdentityFailureReason reason) => reason switch
    {
        PhysicalIdentityFailureReason.UnsupportedFilesystem => Error.Conflict(
            "workspaces.physical_identity_unsupported_filesystem",
            "This repository's filesystem does not support the identity checks workspace preparation requires."),
        PhysicalIdentityFailureReason.PathInaccessible => Error.Conflict(
            "workspaces.physical_identity_path_inaccessible", "This project's path could not be verified."),
        _ => Error.Conflict("workspaces.physical_identity_not_resolved", "This project's physical identity has not been verified yet."),
    };

    private static Error MapRootOutcome(RepositoryRootInspectionOutcome outcome) => outcome switch
    {
        RepositoryRootInspectionOutcome.NotAbsolute =>
            Error.Conflict("projects.path_not_absolute", "The path must be a fully qualified local path."),
        RepositoryRootInspectionOutcome.RemoteRootNotSupported =>
            Error.Conflict("projects.remote_root_not_supported", "UNC or network paths are not supported."),
        RepositoryRootInspectionOutcome.FilesystemRootNotSupported =>
            Error.Conflict("projects.root_not_supported", "The drive or share root itself cannot be registered."),
        RepositoryRootInspectionOutcome.PathNotFound =>
            Error.NotFound("projects.path_not_found", "This path does not exist."),
        RepositoryRootInspectionOutcome.PathInaccessible =>
            Error.Conflict("projects.path_inaccessible", "This path could not be inspected."),
        RepositoryRootInspectionOutcome.ReparsePointNotSupported =>
            Error.Conflict("projects.reparse_point_not_supported", "A symbolic link or junction cannot be registered directly."),
        _ => Error.Conflict("workspaces.preparation_failed", "This workspace could not be prepared."),
    };

    private static Error MapGitOutcome(GitRepositoryInspectionOutcome outcome) => outcome switch
    {
        GitRepositoryInspectionOutcome.GitUnavailable =>
            Error.Conflict("workspaces.git_unavailable", "Git is not currently available on this host."),
        GitRepositoryInspectionOutcome.NotAGitRepository =>
            Error.Conflict("projects.not_a_git_repository", "This folder is not a Git repository."),
        GitRepositoryInspectionOutcome.BareRepositoryNotSupported =>
            Error.Conflict("projects.bare_repository_not_supported", "A bare Git repository cannot be registered."),
        GitRepositoryInspectionOutcome.LinkedWorktreeNotSupported =>
            Error.Conflict("projects.linked_worktree_not_supported", "This path is a linked Git worktree, not the main repository."),
        GitRepositoryInspectionOutcome.NotTopLevelRoot =>
            Error.Conflict("projects.not_top_level_root", "This folder is not the top-level root of its Git repository."),
        GitRepositoryInspectionOutcome.RepositoryChangedDuringInspection =>
            Error.Conflict("projects.repository_changed_during_inspection", "The repository changed while it was being inspected. Try again."),
        GitRepositoryInspectionOutcome.InvalidHeadState =>
            Error.Conflict("projects.invalid_head_state", "This repository's HEAD is in an unrecognized or corrupt state."),
        GitRepositoryInspectionOutcome.GitInvocationTimedOut =>
            Error.Conflict("projects.git_invocation_timed_out", "Inspecting this repository took too long."),
        _ => Error.Conflict("workspaces.preparation_failed", "This workspace could not be prepared."),
    };

    private static Error MapCreationOutcome(GitWorktreeCreationOutcome outcome) => outcome switch
    {
        GitWorktreeCreationOutcome.PathAlreadyExists =>
            Error.Conflict("workspaces.path_already_exists", "A workspace already exists at the computed path."),
        GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository =>
            Error.Conflict("workspaces.path_overlaps_repository", "This workspace's computed path conflicts with the repository's own path."),
        GitWorktreeCreationOutcome.GitUnavailable =>
            Error.Conflict("workspaces.git_unavailable", "Git is not currently available on this host."),
        GitWorktreeCreationOutcome.GitInvocationTimedOut =>
            Error.Conflict("workspaces.git_invocation_timed_out", "Creating this workspace took too long."),
        _ => Error.Conflict("workspaces.git_invocation_failed", "This workspace could not be created."),
    };
}
