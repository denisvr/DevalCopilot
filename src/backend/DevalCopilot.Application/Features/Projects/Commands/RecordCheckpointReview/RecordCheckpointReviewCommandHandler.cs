using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;

public sealed class RecordCheckpointReviewCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider)
    : ICommandHandler<RecordCheckpointReviewCommand, Result<RecordCheckpointReviewCommandResult>>
{
    /// <summary>Manual transaction is required so the fresh Git capture is never performed
    /// while the EF transaction pipeline holds an ambient transaction.</summary>
    public async Task<Result<RecordCheckpointReviewCommandResult>> HandleAsync(
        RecordCheckpointReviewCommand command, CancellationToken cancellationToken)
    {
        if (command.Decision == ReviewDecision.Pending && command.VerificationExecutionId.HasValue)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.pending_cannot_include_evidence", "A pending review cannot include verification evidence."));
        }

        var projectExists = await dbContext.Projects.AnyAsync(project => project.Id == command.ProjectId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == command.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (!projectExists || workspace is null)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.NotFound("reviews.not_found", "The project or isolated workspace was not found."));
        }

        if (workspace.Status != WorkspaceStatus.Ready || !await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.workspace_not_ready", "A ready, owned workspace is required to record a review."));
        }

        var checkpoint = await dbContext.GitCheckpoints.SingleOrDefaultAsync(
            candidate => candidate.Id == command.GitCheckpointId && candidate.WorkspaceId == workspace.Id, cancellationToken);
        var currentCheckpointId = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null || checkpoint.Id != currentCheckpointId)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.checkpoint_not_current", "Only the current source checkpoint may be reviewed."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var execution = command.Decision == ReviewDecision.Pending
            ? null
            : await dbContext.VerificationExecutions.SingleOrDefaultAsync(
                candidate => candidate.Id == command.VerificationExecutionId
                    && candidate.ProjectId == command.ProjectId
                    && candidate.GitWorkspaceId == workspace.Id
                    && candidate.GitCheckpointId == checkpoint.Id,
                cancellationToken);
        if (command.Decision != ReviewDecision.Pending && execution is null)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.NotFound("reviews.evidence_not_found", "The verification evidence was not found for this checkpoint."));
        }

        if (execution is not null && execution.Status == VerificationExecutionStatus.Running)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.evidence_not_terminal", "A completed verification execution is required for this review decision."));
        }

        if (command.Decision == ReviewDecision.Approved && execution?.Status != VerificationExecutionStatus.Passed)
        {
            return Result<RecordCheckpointReviewCommandResult>.Failure(
                Error.Conflict("reviews.approval_requires_passed_verification", "Approval requires a passed verification execution."));
        }

        var reviewId = Guid.NewGuid();
        var evidenceMembers = execution is null
            ? []
            : new[]
            {
                CheckpointReviewEvidence.Observe(
                    Guid.NewGuid(),
                    reviewId,
                    execution.VerificationCommandId,
                    execution.Id,
                    execution.ExecutionNumber,
                    execution.CheckpointFingerprintSha256,
                    execution.Status,
                    execution.Outcome,
                    execution.ExitCode),
            };

        var review = CheckpointReview.Record(
            reviewId,
            command.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.CheckpointNumber,
            checkpoint.FingerprintSha256,
            command.ActorKind,
            command.Decision,
            timeProvider.GetUtcNow(),
            evidenceMembers);
        dbContext.CheckpointReviews.Add(review);
        dbContext.CheckpointReviewEvidence.AddRange(evidenceMembers);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordCheckpointReviewCommandResult>.Success(new(review.Id, review.Decision));
    }
}
