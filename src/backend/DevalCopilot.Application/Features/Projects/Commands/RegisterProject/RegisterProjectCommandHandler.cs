using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RegisterProject;

/// <summary>
/// Pure orchestration over two typed Infrastructure ports and persistence — no
/// <c>System.IO</c>, no <c>Path.*</c>, no filesystem/process exception handling. Every failure
/// reason below is a closed enum value returned by a port; the message text attached to each
/// is fixed here and never includes anything the port itself did not return.
/// </summary>
public sealed class RegisterProjectCommandHandler(
    IDevalCopilotDbContext dbContext,
    IRepositoryRootPathInspector rootPathInspector,
    IGitRepositoryInspector gitRepositoryInspector,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterProjectCommand, Result<RegisterProjectCommandResult>>
{
    public async Task<Result<RegisterProjectCommandResult>> HandleAsync(
        RegisterProjectCommand command, CancellationToken cancellationToken)
    {
        var rootResult = rootPathInspector.Inspect(command.RequestedPath);
        if (rootResult.Outcome != RepositoryRootInspectionOutcome.Success || rootResult.Candidate is null)
        {
            return Result<RegisterProjectCommandResult>.Failure(MapRootOutcome(rootResult.Outcome));
        }

        var candidate = rootResult.Candidate;

        // The same pure string transform Project.Register uses internally — never a Path/IO
        // call, so this remains a legitimate Application-layer computation.
        var registrationIdentityKey = candidate.CanonicalPath.ToUpperInvariant();

        var alreadyRegistered = await dbContext.Projects
            .AnyAsync(project => project.RegistrationIdentityKey == registrationIdentityKey, cancellationToken);
        if (alreadyRegistered)
        {
            return Result<RegisterProjectCommandResult>.Failure(
                Error.Conflict("projects.already_registered", "This path is already registered."));
        }

        var gitSnapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.Capability == Capability.Git, cancellationToken);
        if (gitSnapshot is null || gitSnapshot.ReasonCode != CapabilityProbeReason.None)
        {
            return Result<RegisterProjectCommandResult>.Failure(
                Error.Conflict("projects.git_unavailable", "Git is not currently available on this host."));
        }

        var gitResult = await gitRepositoryInspector.InspectAsync(candidate, cancellationToken);
        if (gitResult.Outcome != GitRepositoryInspectionOutcome.Success)
        {
            return Result<RegisterProjectCommandResult>.Failure(MapGitOutcome(gitResult.Outcome));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var project = Project.Register(Guid.NewGuid(), command.Name, candidate.CanonicalPath, nowUtc);
        var baselineNumber = project.ReserveBaselineNumber();
        var baseline = RepositoryBaseline.Capture(
            Guid.NewGuid(),
            project.Id,
            baselineNumber,
            nowUtc,
            gitResult.HeadState!.Value,
            gitResult.BranchName,
            gitResult.HeadCommitSha,
            gitResult.IsDirty);

        dbContext.Projects.Add(project);
        dbContext.RepositoryBaselines.Add(baseline);

        return Result<RegisterProjectCommandResult>.Success(new RegisterProjectCommandResult(project.Id));
    }

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
        _ => Error.Conflict("projects.registration_failed", "This path could not be registered."),
    };

    private static Error MapGitOutcome(GitRepositoryInspectionOutcome outcome) => outcome switch
    {
        GitRepositoryInspectionOutcome.GitUnavailable =>
            Error.Conflict("projects.git_unavailable", "Git is not currently available on this host."),
        GitRepositoryInspectionOutcome.NotAGitRepository =>
            Error.Conflict("projects.not_a_git_repository", "This folder is not a Git repository."),
        GitRepositoryInspectionOutcome.BareRepositoryNotSupported =>
            Error.Conflict("projects.bare_repository_not_supported", "A bare Git repository cannot be registered."),
        GitRepositoryInspectionOutcome.LinkedWorktreeNotSupported =>
            Error.Conflict("projects.linked_worktree_not_supported", "This path is a linked Git worktree, not the main repository. Register the main repository instead."),
        GitRepositoryInspectionOutcome.NotTopLevelRoot =>
            Error.Conflict("projects.not_top_level_root", "This folder is not the top-level root of its Git repository."),
        GitRepositoryInspectionOutcome.RepositoryChangedDuringInspection =>
            Error.Conflict("projects.repository_changed_during_inspection", "The repository changed while it was being inspected. Try again."),
        GitRepositoryInspectionOutcome.InvalidHeadState =>
            Error.Conflict("projects.invalid_head_state", "This repository's HEAD is in an unrecognized or corrupt state."),
        GitRepositoryInspectionOutcome.GitInvocationTimedOut =>
            Error.Conflict("projects.git_invocation_timed_out", "Inspecting this repository took too long."),
        _ => Error.Conflict("projects.registration_failed", "This path could not be registered."),
    };
}
