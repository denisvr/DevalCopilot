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
    /// <summary>Manual transaction is required so the fresh Git capture is never performed while a transaction is open.
    /// Untracked reads decide the source before the capture; after it, one short write-locked transaction re-reads every
    /// authority fact untracked, applies the same gates and records the review with its evidence member atomically. The Git
    /// observation is a point-in-time read of the working tree: it does not freeze the filesystem through the commit, so a
    /// later source change leaves the recorded review as a stale historical fact, never a retroactive one.</summary>
    public async Task<Result<RecordCheckpointReviewCommandResult>> HandleAsync(
        RecordCheckpointReviewCommand command, CancellationToken cancellationToken)
    {
        if (command.Decision == ReviewDecision.Pending && command.VerificationExecutionId.HasValue)
        {
            return Failure(Error.Conflict("reviews.pending_cannot_include_evidence", "A pending review cannot include verification evidence."));
        }

        var early = await ReadSourceAsync(command, cancellationToken);
        if (early.Source is not { } observed)
        {
            return Failure(early.Error!);
        }

        var evidence = await evidenceReader.CaptureAsync(observed.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != observed.FingerprintSha256)
        {
            return Failure(CheckpointNotCurrent());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // A self-referential no-op write is the transaction's first statement, so it holds the database write lock before any
        // authority is re-read: no other connection can commit a competing change until this one commits or rolls back.
        var locked = await dbContext.Projects
            .Where(candidate => candidate.Id == command.ProjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Name, candidate => candidate.Name), cancellationToken);
        if (locked != 1)
        {
            return Failure(NotFound());
        }

        var fresh = await ReadSourceAsync(command, cancellationToken);
        if (fresh.Error is { } freshError)
        {
            return Failure(freshError);
        }

        if (fresh.Source != observed)
        {
            return Failure(CheckpointNotCurrent());
        }

        var execution = command.Decision == ReviewDecision.Pending
            ? null
            : await dbContext.VerificationExecutions
                .AsNoTracking()
                .Where(candidate => candidate.Id == command.VerificationExecutionId
                    && candidate.ProjectId == command.ProjectId
                    && candidate.GitWorkspaceId == observed.WorkspaceId
                    && candidate.GitCheckpointId == observed.CheckpointId
                    && candidate.CheckpointFingerprintSha256 == observed.FingerprintSha256)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.VerificationCommandId,
                    candidate.ExecutionNumber,
                    candidate.CheckpointFingerprintSha256,
                    candidate.Status,
                    candidate.Outcome,
                    candidate.ExitCode,
                })
                .SingleOrDefaultAsync(cancellationToken);
        if (command.Decision != ReviewDecision.Pending && execution is null)
        {
            return Failure(Error.NotFound("reviews.evidence_not_found", "The verification evidence was not found for this checkpoint."));
        }

        if (execution is not null && execution.Status == VerificationExecutionStatus.Running)
        {
            return Failure(EvidenceNotTerminal());
        }

        if (command.Decision == ReviewDecision.Approved && execution?.Status != VerificationExecutionStatus.Passed)
        {
            return Failure(Error.Conflict("reviews.approval_requires_passed_verification", "Approval requires a passed verification execution."));
        }

        var reviewId = Guid.NewGuid();
        CheckpointReviewEvidence[] evidenceMembers;
        CheckpointReview review;
        try
        {
            evidenceMembers = execution is null
                ? []
                :
                [
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
                ];
            review = CheckpointReview.Record(
                reviewId,
                command.ProjectId,
                observed.WorkspaceId,
                observed.CheckpointId,
                observed.CheckpointNumber,
                observed.FingerprintSha256,
                command.ActorKind,
                command.Decision,
                timeProvider.GetUtcNow(),
                evidenceMembers);
        }
        catch (ArgumentException)
        {
            // Stored terminal evidence that is not internally coherent is refused, never recorded and never a request failure.
            return Failure(EvidenceNotTerminal());
        }

        dbContext.CheckpointReviews.Add(review);
        dbContext.CheckpointReviewEvidence.AddRange(evidenceMembers);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<RecordCheckpointReviewCommandResult>.Success(new(review.Id, review.Decision));
    }

    /// <summary>The project, its latest workspace and that workspace's exact current checkpoint, read untracked and decided by
    /// the existing gates: a ready workspace with an active lease, and a selected checkpoint that is the current one.</summary>
    private async Task<SourceRead> ReadSourceAsync(RecordCheckpointReviewCommand command, CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Projects.AsNoTracking().AnyAsync(project => project.Id == command.ProjectId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == command.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .Select(candidate => new { candidate.Id, candidate.Status, candidate.WorkspacePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (!projectExists || workspace is null)
        {
            return new SourceRead(NotFound(), null);
        }

        if (workspace.Status != WorkspaceStatus.Ready || !await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return new SourceRead(
                Error.Conflict("reviews.workspace_not_ready", "A ready, owned workspace is required to record a review."), null);
        }

        var current = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => new { candidate.Id, candidate.CheckpointNumber, candidate.FingerprintSha256 })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null || current.Id != command.GitCheckpointId)
        {
            return new SourceRead(
                Error.Conflict("reviews.checkpoint_not_current", "Only the current source checkpoint may be reviewed."), null);
        }

        return new SourceRead(
            null, new SourceAuthority(workspace.Id, workspace.WorkspacePath, current.Id, current.CheckpointNumber, current.FingerprintSha256));
    }

    private static Result<RecordCheckpointReviewCommandResult> Failure(Error error) => Result<RecordCheckpointReviewCommandResult>.Failure(error);

    private static Error NotFound() => Error.NotFound("reviews.not_found", "The project or isolated workspace was not found.");

    private static Error CheckpointNotCurrent() =>
        Error.Conflict("reviews.checkpoint_not_current", "The selected source checkpoint is no longer current.");

    private static Error EvidenceNotTerminal() =>
        Error.Conflict("reviews.evidence_not_terminal", "A completed verification execution is required for this review decision.");

    private sealed record SourceAuthority(
        Guid WorkspaceId, string WorkspacePath, Guid CheckpointId, int CheckpointNumber, string FingerprintSha256);

    private sealed record SourceRead(Error? Error, SourceAuthority? Source);
}
