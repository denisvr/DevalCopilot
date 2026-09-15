using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;

public sealed class GetGitCheckpointDiffQueryHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader)
    : IQueryHandler<GetGitCheckpointDiffQuery, Result<GetGitCheckpointDiffQueryResult>>
{
    public async Task<Result<GetGitCheckpointDiffQueryResult>> HandleAsync(
        GetGitCheckpointDiffQuery query, CancellationToken cancellationToken)
    {
        var checkpoint = await (
                from candidate in dbContext.GitCheckpoints.AsNoTracking()
                join workspace in dbContext.GitWorkspaces.AsNoTracking() on candidate.WorkspaceId equals workspace.Id
                where candidate.Id == query.CheckpointId && workspace.ProjectId == query.ProjectId
                select new { candidate.FingerprintSha256, WorkspaceId = workspace.Id, workspace.WorkspacePath, workspace.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<GetGitCheckpointDiffQueryResult>.Failure(
                Error.NotFound("git_checkpoints.not_found", "This source checkpoint does not exist for this project."));
        }

        if (checkpoint.Status != WorkspaceStatus.Ready)
        {
            return Result<GetGitCheckpointDiffQueryResult>.Failure(
                Error.Conflict("workspaces.not_ready", "This workspace is not ready for source evidence inspection."));
        }

        var hasActiveLease = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == checkpoint.WorkspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!hasActiveLease)
        {
            return Result<GetGitCheckpointDiffQueryResult>.Failure(
                Error.Conflict("workspaces.lease_not_active", "This workspace is no longer owned for source evidence inspection."));
        }

        var current = await evidenceReader.CaptureAsync(checkpoint.WorkspacePath, cancellationToken);
        if (current.Outcome != GitWorkspaceEvidenceOutcome.Success)
        {
            return Result<GetGitCheckpointDiffQueryResult>.Failure(MapEvidenceFailure(current.Outcome));
        }

        if (!string.Equals(current.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Result<GetGitCheckpointDiffQueryResult>.Failure(
                Error.Conflict("git_evidence.stale_checkpoint", "Source changed since this checkpoint. Capture new evidence before inspecting the complete diff."));
        }

        return Result<GetGitCheckpointDiffQueryResult>.Success(new(current.FingerprintSha256!, current.CompleteDiff!));
    }

    private static Error MapEvidenceFailure(GitWorkspaceEvidenceOutcome outcome) => outcome switch
    {
        GitWorkspaceEvidenceOutcome.GitUnavailable => Error.Conflict("git_evidence.git_unavailable", "Git is not available on this host."),
        GitWorkspaceEvidenceOutcome.GitInvocationTimedOut => Error.Conflict("git_evidence.timed_out", "Source evidence capture took too long."),
        GitWorkspaceEvidenceOutcome.EvidenceTooLarge => Error.Conflict("git_evidence.too_large", "Source evidence exceeds this checkpoint's bounded capture limit."),
        GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture => Error.Conflict("git_evidence.changed_during_capture", "Source changed while evidence was being captured. Capture a new checkpoint."),
        GitWorkspaceEvidenceOutcome.InvalidGitState => Error.Conflict("git_evidence.invalid_state", "Git could not provide a valid source state for this workspace."),
        _ => Error.Conflict("git_evidence.capture_failed", "Source evidence could not be captured."),
    };
}
