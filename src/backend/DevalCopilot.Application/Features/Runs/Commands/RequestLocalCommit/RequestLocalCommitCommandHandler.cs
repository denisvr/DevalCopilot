using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RequestLocalCommit;

/// <summary>
/// Admits at most one local-commit operation for a run (ADR-0029). Untracked reads decide every gate before any Git work;
/// <see cref="ILocalCommitPreparer"/> then builds the immutable commit outside any transaction; one short write-locked
/// transaction finally reserves the workspace as Committing, re-reads the complete authority and persists the operation, its
/// authority membership and the admission event together. A refusal before that transaction writes nothing, a replay of the same
/// operation and normalized request returns the recorded operation without any Git execution, and anything else conflicts.
/// </summary>
public sealed class RequestLocalCommitCommandHandler(
    IDevalCopilotDbContext dbContext,
    ILocalCommitPreparer preparer,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<RequestLocalCommitCommand, Result<LocalCommitOperationView>>
{
    public async Task<Result<LocalCommitOperationView>> HandleAsync(
        RequestLocalCommitCommand command, CancellationToken cancellationToken)
    {
        if (!LocalCommitMessagePolicy.TryNormalize(command.Message, out var normalizedMessage))
        {
            return Result<LocalCommitOperationView>.Failure(
                Error.Failure("validation.invalid", "The commit message is not acceptable."));
        }

        var requestSha256 = LocalCommitMessagePolicy.ComputeRequestSha256(
            command.CheckpointId, command.CodeReviewAttemptId, command.HumanCheckpointReviewId, normalizedMessage);
        var reader = new LocalCommitAuthorityReader(dbContext);
        var selection = new LocalCommitAuthorityReader.Selection(
            command.CheckpointId, command.CodeReviewAttemptId, command.HumanCheckpointReviewId);

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == command.RunId, cancellationToken))
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.RunNotFound());
        }

        var replay = await ReplayAsync(command, requestSha256, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var early = await reader.ReadAsync(command.RunId, selection, WorkspaceStatus.Ready, null, cancellationToken);
        if (early.Authority is not { } authority)
        {
            return Result<LocalCommitOperationView>.Failure(early.Error!);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var prepared = await preparer.PrepareAsync(
            new LocalCommitPreparationRequest(
                command.OperationId,
                authority.ProjectCanonicalPath,
                authority.Workspace.WorkspacePath,
                authority.Workspace.BranchName,
                authority.ExpectedParentCommitSha,
                authority.Checkpoint.FingerprintSha256,
                authority.ChangedFiles
                    .Select(file => new LocalCommitChangedPath(file.Path, file.IndexStatus, file.WorkTreeStatus))
                    .ToArray(),
                normalizedMessage,
                new LocalCommitOwnership(
                    authority.Workspace.Id,
                    authority.Workspace.ProjectId,
                    authority.Lease.Id,
                    authority.Lease.PhysicalVolumeSerialNumber,
                    Convert.ToHexString(authority.Lease.PhysicalFileId)),
                nowUtc),
            cancellationToken);
        if (prepared.Outcome != LocalCommitPreparationOutcome.Prepared || prepared.Facts is not { } facts)
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.Refused(prepared.Outcome));
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The reservation is the transaction's first statement: one atomic Ready-to-Committing UPDATE that also takes the
        // database write lock, so no competing claim, capture, review or recipe write can commit between the re-read below and
        // this operation's own commit. A refusal rolls the reservation back with the transaction.
        var reserved = await dbContext.GitWorkspaces
            .Where(candidate => candidate.Id == authority.Workspace.Id && candidate.Status == WorkspaceStatus.Ready)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, WorkspaceStatus.Committing), cancellationToken);
        if (reserved != 1)
        {
            return await ReplayAsync(command, requestSha256, cancellationToken)
                ?? Result<LocalCommitOperationView>.Failure(LocalCommitErrors.WorkspaceNotReady());
        }

        var fresh = await reader.ReadAsync(command.RunId, selection, WorkspaceStatus.Committing, null, cancellationToken);
        if (fresh.Authority is not { } current)
        {
            return Result<LocalCommitOperationView>.Failure(fresh.Error!);
        }

        if (!string.Equals(current.AuthoritySha256, authority.AuthoritySha256, StringComparison.Ordinal))
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.AuthorityChanged());
        }

        if (await dbContext.LocalCommitOperations.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == command.RunId || candidate.Id == command.OperationId, cancellationToken))
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.OperationConflict());
        }

        var members = new List<LocalCommitAuthorityMember>();
        foreach (var member in current.Verification)
        {
            members.Add(LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), command.OperationId, LocalCommitAuthorityMemberKind.Verification, member.Sequence,
                member.Execution.Id, member.Command.Id, member.Digest));
        }

        for (var index = 0; index < current.HumanDecisions.Count; index++)
        {
            var decision = current.HumanDecisions[index];
            members.Add(LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), command.OperationId, LocalCommitAuthorityMemberKind.HumanReview, index,
                decision.Review.Id, null, decision.Digest));
        }

        LocalCommitOperation operation;
        try
        {
            operation = LocalCommitOperation.Prepare(
                new LocalCommitOperation.PreparedFacts(
                    command.OperationId,
                    command.RunId,
                    current.Workspace.ProjectId,
                    current.Workspace.Id,
                    current.Lease.Id,
                    current.Checkpoint.Id,
                    current.Checkpoint.CheckpointNumber,
                    current.Checkpoint.FingerprintSha256,
                    current.ReviewAttempt.Id,
                    current.ApprovalMessage.Id,
                    current.ExecutionReport.Id,
                    current.AgentReview.Id,
                    current.HumanReview.Id,
                    requestSha256,
                    current.AuthoritySha256,
                    normalizedMessage,
                    current.Workspace.BranchName,
                    current.ExpectedParentCommitSha,
                    facts.TreeSha,
                    facts.CommitSha,
                    facts.AuthorName,
                    facts.AuthorEmail,
                    facts.CommitTimeUnixSeconds,
                    facts.IndexPreimageSha256,
                    facts.PreparedIndexSha256,
                    facts.PreparedIndexRelativePath,
                    facts.ChangedPathCount,
                    facts.TotalBytes,
                    nowUtc),
                members);
        }
        catch (ArgumentException)
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.Refused(LocalCommitPreparationOutcome.GitFailed));
        }

        dbContext.LocalCommitOperations.Add(operation);
        dbContext.LocalCommitAuthorityMembers.AddRange(members);
        var admitted = RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            attemptId: null,
            RunEventType.LocalCommitAdmitted,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new
            {
                operationId = operation.Id,
                checkpointId = operation.GitCheckpointId,
                checkpointNumber = operation.CheckpointNumber,
                parent = operation.ParentCommitSha,
                tree = operation.TreeSha,
                commit = operation.CommitSha,
            }),
            nowUtc);
        dbContext.Events.Add(admitted);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return await ReplayAsync(command, requestSha256, cancellationToken)
                ?? Result<LocalCommitOperationView>.Failure(LocalCommitErrors.OperationConflict());
        }

        await LocalCommitNotification.NotifyAsync(eventNotifier, command.RunId, admitted.Sequence, cancellationToken);

        return Result<LocalCommitOperationView>.Success(LocalCommitOperationView.From(operation));
    }

    /// <summary>The recorded operation for an identical operation UUID and normalized request, a conflict for anything else the
    /// run or the UUID already owns, and null when neither exists yet.</summary>
    private async Task<Result<LocalCommitOperationView>?> ReplayAsync(
        RequestLocalCommitCommand command, string requestSha256, CancellationToken cancellationToken)
    {
        var existing = await dbContext.LocalCommitOperations.AsNoTracking()
            .Where(candidate => candidate.RunId == command.RunId || candidate.Id == command.OperationId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            return null;
        }

        return existing.Count == 1 && existing[0].Id == command.OperationId && existing[0].RunId == command.RunId
            && string.Equals(existing[0].RequestSha256, requestSha256, StringComparison.Ordinal)
                ? Result<LocalCommitOperationView>.Success(LocalCommitOperationView.From(existing[0]))
                : Result<LocalCommitOperationView>.Failure(LocalCommitErrors.OperationConflict());
    }
}
