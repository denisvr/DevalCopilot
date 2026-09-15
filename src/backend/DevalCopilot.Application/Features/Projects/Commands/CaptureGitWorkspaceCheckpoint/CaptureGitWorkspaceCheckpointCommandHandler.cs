using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;

/// <summary>Captures a coherent workspace snapshot through the hardened reader, then commits
/// only bounded metadata and paths. Complete diff text stays in transient process/query memory.
/// <see cref="IManualTransactionCommand{TResult}"/> is deliberate: no EF transaction may span
/// the Git evidence capture.</summary>
public sealed class CaptureGitWorkspaceCheckpointCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider)
    : ICommandHandler<CaptureGitWorkspaceCheckpointCommand, Result<CaptureGitWorkspaceCheckpointCommandResult>>
{
    public async Task<Result<CaptureGitWorkspaceCheckpointCommandResult>> HandleAsync(
        CaptureGitWorkspaceCheckpointCommand command, CancellationToken cancellationToken)
    {
        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == command.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (workspace is null)
        {
            return Result<CaptureGitWorkspaceCheckpointCommandResult>.Failure(
                Error.NotFound("workspaces.not_found", "This project has no prepared workspace."));
        }

        if (workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CaptureGitWorkspaceCheckpointCommandResult>.Failure(
                Error.Conflict("workspaces.not_ready", "This workspace is not ready for source evidence capture."));
        }

        var hasActiveLease = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!hasActiveLease)
        {
            return Result<CaptureGitWorkspaceCheckpointCommandResult>.Failure(
                Error.Conflict("workspaces.lease_not_active", "This workspace is no longer owned for source evidence capture."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success)
        {
            return Result<CaptureGitWorkspaceCheckpointCommandResult>.Failure(MapEvidenceFailure(evidence.Outcome));
        }

        var checkpointId = Guid.NewGuid();
        var changedFiles = evidence.ChangedPaths
            .Select(path => GitChangedFile.Observe(
                Guid.NewGuid(), checkpointId, path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus))
            .ToArray();
        var checkpoint = GitCheckpoint.Capture(
            checkpointId,
            workspace.Id,
            workspace.ReserveCheckpointNumber(),
            timeProvider.GetUtcNow(),
            evidence.HeadCommitSha!,
            evidence.FingerprintSha256!,
            changedFiles);

        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.GitChangedFiles.AddRange(changedFiles);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<CaptureGitWorkspaceCheckpointCommandResult>.Success(new(
            checkpoint.Id,
            checkpoint.CheckpointNumber,
            checkpoint.HeadCommitSha,
            checkpoint.FingerprintSha256,
            changedFiles.Length));
    }

    private static Error MapEvidenceFailure(GitWorkspaceEvidenceOutcome outcome) => outcome switch
    {
        GitWorkspaceEvidenceOutcome.GitUnavailable =>
            Error.Conflict("git_evidence.git_unavailable", "Git is not available on this host."),
        GitWorkspaceEvidenceOutcome.GitInvocationTimedOut =>
            Error.Conflict("git_evidence.timed_out", "Source evidence capture took too long."),
        GitWorkspaceEvidenceOutcome.EvidenceTooLarge =>
            Error.Conflict("git_evidence.too_large", "Source evidence exceeds this checkpoint's bounded capture limit."),
        GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture =>
            Error.Conflict("git_evidence.changed_during_capture", "Source changed while evidence was being captured. Capture a new checkpoint."),
        GitWorkspaceEvidenceOutcome.InvalidGitState =>
            Error.Conflict("git_evidence.invalid_state", "Git could not provide a valid source state for this workspace."),
        _ => Error.Conflict("git_evidence.capture_failed", "Source evidence could not be captured."),
    };
}
