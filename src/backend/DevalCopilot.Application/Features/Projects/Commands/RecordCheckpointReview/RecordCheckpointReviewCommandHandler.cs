using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies;
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
    /// authority fact untracked (the source, the selected executions and, for a complete-set approval, the enabled recipes with their
    /// latest executions), applies the same gates and records the review with all of its evidence members atomically. A changed
    /// selection is refused, never retargeted. The Git observation is a point-in-time read of the working tree: it does not freeze the
    /// filesystem through the commit, so a later source change leaves the recorded review as a stale historical fact, never a
    /// retroactive one.</summary>
    public async Task<Result<RecordCheckpointReviewCommandResult>> HandleAsync(
        RecordCheckpointReviewCommand command, CancellationToken cancellationToken)
    {
        var selection = CheckpointReviewEvidenceSelection.Resolve(command);
        if (selection.Error is { } selectionError)
        {
            return Failure(selectionError);
        }

        var early = await CheckpointReviewSource.ReadAsync(dbContext, command.ProjectId, command.GitCheckpointId, cancellationToken);
        if (early.Source is not { } observed)
        {
            return Failure(early.Error!);
        }

        var evidence = await evidenceReader.CaptureAsync(observed.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != observed.FingerprintSha256)
        {
            return Failure(CheckpointReviewSource.CheckpointNotCurrent());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // A self-referential no-op write is the transaction's first statement, so it holds the database write lock before any
        // authority is re-read: no other connection can commit a competing change until this one commits or rolls back.
        var locked = await dbContext.Projects
            .Where(candidate => candidate.Id == command.ProjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Name, candidate => candidate.Name), cancellationToken);
        if (locked != 1)
        {
            return Failure(CheckpointReviewSource.NotFound());
        }

        var fresh = await CheckpointReviewSource.ReadAsync(dbContext, command.ProjectId, command.GitCheckpointId, cancellationToken);
        if (fresh.Error is { } freshError)
        {
            return Failure(freshError);
        }

        if (fresh.Source != observed)
        {
            return Failure(CheckpointReviewSource.CheckpointNotCurrent());
        }

        var resolved = command.Decision == ReviewDecision.Pending
            ? new ResolvedEvidence(null, [])
            : await ResolveEvidenceAsync(command, selection, observed, cancellationToken);
        if (resolved.Error is { } evidenceError)
        {
            return Failure(evidenceError);
        }

        var reviewId = Guid.NewGuid();
        CheckpointReviewEvidence[] evidenceMembers;
        CheckpointReview review;
        try
        {
            evidenceMembers = resolved.Facts.Select(fact => CheckpointReviewEvidence.Observe(
                Guid.NewGuid(),
                reviewId,
                fact.VerificationCommandId,
                fact.Id,
                fact.ExecutionNumber,
                fact.CheckpointFingerprintSha256,
                fact.Status,
                fact.Outcome,
                fact.ExitCode)).ToArray();
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

    /// <summary>The selected executions of a decided review, read fresh and untracked under the write lock, each bound to the exact
    /// owned current checkpoint and fingerprint, in the host's canonical CommandNumber order. An approval in the set form additionally
    /// requires exactly the complete current verification set; the legacy single form keeps its checkpoint-bound meaning.</summary>
    private async Task<ResolvedEvidence> ResolveEvidenceAsync(
        RecordCheckpointReviewCommand command,
        CheckpointReviewEvidenceSelection.Selection selection,
        CheckpointReviewSource.Authority observed,
        CancellationToken cancellationToken)
    {
        if (selection.ExecutionIds.Count == 0)
        {
            return ResolvedEvidence.Refused(EvidenceNotFound());
        }

        var ids = selection.ExecutionIds.ToArray();
        var found = await dbContext.VerificationExecutions
            .AsNoTracking()
            .Where(candidate => ids.Contains(candidate.Id)
                && candidate.ProjectId == command.ProjectId
                && candidate.GitWorkspaceId == observed.WorkspaceId
                && candidate.GitCheckpointId == observed.CheckpointId
                && candidate.CheckpointFingerprintSha256 == observed.FingerprintSha256)
            .Select(candidate => new EvidenceFact(
                candidate.Id,
                candidate.VerificationCommandId,
                candidate.ExecutionNumber,
                candidate.CheckpointFingerprintSha256,
                candidate.Status,
                candidate.Outcome,
                candidate.ExitCode,
                int.MaxValue))
            .ToListAsync(cancellationToken);
        if (found.Count != ids.Length)
        {
            return ResolvedEvidence.Refused(EvidenceNotFound());
        }

        if (found.Any(fact => fact.Status == VerificationExecutionStatus.Running))
        {
            return ResolvedEvidence.Refused(EvidenceNotTerminal());
        }

        if (command.Decision == ReviewDecision.Approved && found.Any(fact => fact.Status != VerificationExecutionStatus.Passed))
        {
            return ResolvedEvidence.Refused(Error.Conflict(
                "reviews.approval_requires_passed_verification", "Approval requires a passed verification execution."));
        }

        if (found.Select(fact => fact.VerificationCommandId).Distinct().Count() != found.Count)
        {
            return ResolvedEvidence.Refused(Error.Failure(
                CheckpointReviewEvidenceSelection.InvalidCode,
                "A verification execution set must not contain more than one execution of the same recipe."));
        }

        var commandIds = found.Select(fact => fact.VerificationCommandId).ToList();
        var commandNumbers = await dbContext.VerificationCommands
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == command.ProjectId && commandIds.Contains(candidate.Id))
            .Select(candidate => new { candidate.Id, candidate.CommandNumber })
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.CommandNumber, cancellationToken);
        var ordered = found
            .Select(fact => fact with { CommandNumber = commandNumbers.GetValueOrDefault(fact.VerificationCommandId, int.MaxValue) })
            .OrderBy(fact => fact.CommandNumber)
            .ThenBy(fact => fact.VerificationCommandId)
            .ToList();

        if (selection.UsesSet && command.Decision == ReviewDecision.Approved)
        {
            var complete = await CompleteVerificationSet.ReadAsync(dbContext, observed, cancellationToken);
            if (complete.Refusal is not null
                || !complete.Members.Select(member => member.Execution.Id).ToHashSet().SetEquals(ordered.Select(fact => fact.Id)))
            {
                return ResolvedEvidence.Refused(Error.Conflict(
                    "reviews.approval_requires_complete_verification_set",
                    "Approval requires exactly one passed latest execution for every enabled verification recipe."));
            }
        }

        return new ResolvedEvidence(null, ordered);
    }

    private static Result<RecordCheckpointReviewCommandResult> Failure(Error error) => Result<RecordCheckpointReviewCommandResult>.Failure(error);

    private static Error EvidenceNotFound() =>
        Error.NotFound("reviews.evidence_not_found", "The verification evidence was not found for this checkpoint.");

    private static Error EvidenceNotTerminal() =>
        Error.Conflict("reviews.evidence_not_terminal", "A completed verification execution is required for this review decision.");

    private sealed record EvidenceFact(
        Guid Id,
        Guid VerificationCommandId,
        int ExecutionNumber,
        string CheckpointFingerprintSha256,
        VerificationExecutionStatus Status,
        VerificationExecutionOutcome? Outcome,
        int? ExitCode,
        int CommandNumber);

    private sealed record ResolvedEvidence(Error? Error, IReadOnlyList<EvidenceFact> Facts)
    {
        public static ResolvedEvidence Refused(Error error) => new(error, []);
    }
}
