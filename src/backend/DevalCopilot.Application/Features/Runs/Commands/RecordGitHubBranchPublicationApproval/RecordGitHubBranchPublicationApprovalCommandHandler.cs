using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;

/// <summary>
/// Records one immutable branch publication approval and exactly one Human-authored event, atomically (ADR-0048). The scope comes only from
/// the unchanged ADR-0040 and ADR-0043 readers over pinned rows and every client acknowledgement must equal it. Nothing external runs. After
/// an initial coherent reading it enters one short transaction whose first statement is the no-op Run write that takes the database write
/// lock, rereads the whole scope and the approval, requires every source, target and binding value to equal the initial reading, and only
/// then decides. A coherent same-identifier approval with the identical request replays its original record, event and time; any other
/// request for a run that already holds one conflicts; an approval that vanished or changed since the initial reading is unavailable and is
/// never recreated; an incoherent record never replays. The run, lifecycle, stage, clocks, workspace, lease, approvals and budgets are never
/// written. A refusal records nothing; a cancelled or failed save rolls back both inserts; faults propagate.
/// </summary>
public sealed class RecordGitHubBranchPublicationApprovalCommandHandler(
    IDevalCopilotDbContext dbContext,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<RecordGitHubBranchPublicationApprovalCommand, Result<RecordGitHubBranchPublicationApprovalCommandResult>>
{
    public async Task<Result<RecordGitHubBranchPublicationApprovalCommandResult>> HandleAsync(
        RecordGitHubBranchPublicationApprovalCommand command, CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty || command.ApprovalId == Guid.Empty || command.OperationId == Guid.Empty
            || command.TargetId == Guid.Empty || command.BindingId == Guid.Empty || !GitHubObservationTarget.IsCommitSha(command.CommitSha)
            || string.IsNullOrEmpty(command.BranchName) || command.BranchName.Length > GitHubObservationTarget.MaximumBranchNameLength
            || !GitHubRepositoryNodeIdPolicy.IsNodeId(command.RepositoryNodeId))
        {
            return Fail(GitHubBranchPublicationApprovalErrors.InvalidRequest());
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == command.RunId, cancellationToken))
        {
            return Fail(GitHubBranchPublicationApprovalErrors.NotFound());
        }

        var before = await ReadAsync(command.RunId, cancellationToken);
        if (before.Scope is null || before.Approval.Kind == GitHubBranchPublicationApprovalReadingKind.Incoherent
            || (before.Approval.Kind == GitHubBranchPublicationApprovalReadingKind.None && !Acknowledges(command, before.Scope)))
        {
            return Fail(GitHubBranchPublicationApprovalErrors.Unavailable());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it, so it is
        // atomic with the inserts saved at the end. Zero rows means the run no longer exists.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Fail(GitHubBranchPublicationApprovalErrors.NotFound());
        }

        var after = await ReadAsync(command.RunId, cancellationToken);
        if (after.Scope is null || after.Scope != before.Scope
            || after.Approval.Kind == GitHubBranchPublicationApprovalReadingKind.Incoherent)
        {
            return Fail(GitHubBranchPublicationApprovalErrors.Unavailable());
        }

        var scope = after.Scope;
        if (before.Approval.Kind == GitHubBranchPublicationApprovalReadingKind.Coherent
            && (after.Approval.Kind != GitHubBranchPublicationApprovalReadingKind.Coherent || after.Approval.Approval != before.Approval.Approval))
        {
            // The recorded approval vanished or changed since it was read: it is never recreated or replaced.
            return Fail(GitHubBranchPublicationApprovalErrors.Unavailable());
        }

        if (after.Approval is { Kind: GitHubBranchPublicationApprovalReadingKind.Coherent, Approval: { } recorded })
        {
            return Matches(recorded, command, scope) ? Succeed(scope, recorded) : Fail(GitHubBranchPublicationApprovalErrors.Conflict());
        }

        // No approval exists for this run. The acknowledgements must equal the scope read under the lock, and another run's approval may
        // not already own the identifier.
        if (!Acknowledges(command, scope))
        {
            return Fail(GitHubBranchPublicationApprovalErrors.Unavailable());
        }

        if (await dbContext.GitHubBranchPublicationApprovals.AsNoTracking()
                .AnyAsync(candidate => candidate.Id == command.ApprovalId && candidate.RunId != command.RunId, cancellationToken))
        {
            return Fail(GitHubBranchPublicationApprovalErrors.Conflict());
        }

        var approvedAtUtc = timeProvider.GetUtcNow();
        if (approvedAtUtc < scope.Binding.RecordedAtUtc)
        {
            // A clock earlier than the binding's recording is never clamped, retried or papered over.
            return Fail(GitHubBranchPublicationApprovalErrors.Unavailable());
        }

        var approval = GitHubBranchPublicationApproval.Record(
            command.ApprovalId, Guid.NewGuid(), scope.Source.RunId, scope.Source.ProjectId, scope.Source.OperationId,
            scope.Source.WorkspaceId, scope.Source.CommitSha, scope.Source.BranchName, scope.Target.Id, scope.Target.EventId,
            scope.Target.Owner, scope.Target.Repository, scope.Target.RecordedAtUtc, scope.Binding.Id, scope.Binding.EventId,
            scope.Binding.RepositoryNodeId, scope.Binding.Owner, scope.Binding.Repository, scope.Binding.ObservedAtUtc,
            scope.Binding.RecordedAtUtc, approvedAtUtc);
        var recordedEvent = RunEvent.Record(
            approval.EventId,
            scope.Source.RunId,
            attemptId: null,
            RunEventType.GitHubBranchPublicationApproved,
            ParticipantIdentity.ForHuman(),
            GitHubBranchPublicationApprovalEventPayload.Build(approval),
            approvedAtUtc);
        dbContext.GitHubBranchPublicationApprovals.Add(approval);
        dbContext.Events.Add(recordedEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await NotifyAsync(scope.Source.RunId, recordedEvent.Sequence, cancellationToken);

        return Succeed(scope, GitHubBranchPublicationApprovalReader.ToView(approval));
    }

    private static Result<RecordGitHubBranchPublicationApprovalCommandResult> Fail(Error error) =>
        Result<RecordGitHubBranchPublicationApprovalCommandResult>.Failure(error);

    private static Result<RecordGitHubBranchPublicationApprovalCommandResult> Succeed(
        GitHubBranchPublicationScope scope, GitHubBranchPublicationApprovalView approval) =>
        Result<RecordGitHubBranchPublicationApprovalCommandResult>.Success(
            new RecordGitHubBranchPublicationApprovalCommandResult(
                scope.Source.RunId, scope.Source.ToView(), scope.Target, scope.Binding, approval));

    /// <summary>The scope, the one thing the client acknowledges, and whatever the approval reader reports about it; the approval is read
    /// only with the scope it must agree with.</summary>
    private async Task<Reading> ReadAsync(Guid runId, CancellationToken cancellationToken)
    {
        var scopeReading = await GitHubBranchPublicationScopeReader.ReadAsync(dbContext, runId, cancellationToken);
        var approval = await GitHubBranchPublicationApprovalReader.ReadAsync(dbContext, runId, scopeReading, cancellationToken);
        return new Reading(scopeReading.Scope, approval);
    }

    /// <summary>Every acknowledgement equals the scope exactly: the operation, the ordinal lowercase commit, the ordinal branch, the target,
    /// the binding and the ordinal node identifier. Nothing is trimmed, normalized or compared ignoring case.</summary>
    private static bool Acknowledges(RecordGitHubBranchPublicationApprovalCommand command, GitHubBranchPublicationScope scope) =>
        command.OperationId == scope.Source.OperationId
        && string.Equals(command.CommitSha, scope.Source.CommitSha, StringComparison.Ordinal)
        && string.Equals(command.BranchName, scope.Source.BranchName, StringComparison.Ordinal)
        && command.TargetId == scope.Target.Id
        && command.BindingId == scope.Binding.Id
        && string.Equals(command.RepositoryNodeId, scope.Binding.RepositoryNodeId, StringComparison.Ordinal);

    private static bool Matches(
        GitHubBranchPublicationApprovalView recorded, RecordGitHubBranchPublicationApprovalCommand command, GitHubBranchPublicationScope scope) =>
        recorded.Id == command.ApprovalId && Acknowledges(command, scope);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// approval into a reported failure. Cancellation still propagates.</summary>
    private async Task NotifyAsync(Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }

    private sealed record Reading(GitHubBranchPublicationScope? Scope, GitHubBranchPublicationApprovalReading Approval);
}
