using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;

/// <summary>
/// Records one immutable GitHub observation target and exactly one Human-authored event, atomically (ADR-0040). No external work
/// belongs here: every decision is made from fresh untracked reads inside one short transaction whose first statement is a no-op write
/// that takes the database write lock, so two requests for the same delivery serialize and the second one finds the first one's
/// committed record. The run, lifecycle, stage, participant, clocks, workspace, lease, approvals and budgets are never written: only
/// the no-op lock, the target and the event belong to this operation.
///
/// <para>
/// The same coherent selection returns the original record, time and event without a second insert; a different selection conflicts.
/// An existing target or event that is not coherent with the pinned source is never a successful replay. A refusal records nothing;
/// a cancelled or failed save rolls back both inserts; an unexpected fault propagates.
/// </para>
/// </summary>
public sealed class RecordGitHubObservationTargetCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<RecordGitHubObservationTargetCommand, Result<RecordGitHubObservationTargetCommandResult>>
{
    public async Task<Result<RecordGitHubObservationTargetCommandResult>> HandleAsync(
        RecordGitHubObservationTargetCommand command, CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty || command.OperationId == Guid.Empty || !GitHubObservationTarget.IsCommitSha(command.CommitSha)
            || !GitHubRepositoryNamePolicy.IsOwner(command.Owner) || !GitHubRepositoryNamePolicy.IsRepository(command.Repository))
        {
            return Result<RecordGitHubObservationTargetCommandResult>.Failure(GitHubObservationTargetErrors.Invalid());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it,
        // so it is atomic with the inserts saved at the end. Zero rows means the run does not exist.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Result<RecordGitHubObservationTargetCommandResult>.Failure(GitHubObservationTargetErrors.NotFound());
        }

        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, command.RunId, cancellationToken);
        if (source is null || source.OperationId != command.OperationId
            || !string.Equals(source.CommitSha, command.CommitSha, StringComparison.Ordinal))
        {
            return Result<RecordGitHubObservationTargetCommandResult>.Failure(GitHubObservationTargetErrors.Unavailable());
        }

        var existing = await GitHubObservationTargetReader.ReadAsync(dbContext, command.RunId, source, cancellationToken);
        if (existing.Kind == GitHubObservationTargetReadingKind.Incoherent)
        {
            return Result<RecordGitHubObservationTargetCommandResult>.Failure(GitHubObservationTargetErrors.Unavailable());
        }

        if (existing is { Kind: GitHubObservationTargetReadingKind.Coherent, Target: { } recorded })
        {
            return string.Equals(recorded.Owner, command.Owner, StringComparison.Ordinal)
                && string.Equals(recorded.Repository, command.Repository, StringComparison.Ordinal)
                    ? Result<RecordGitHubObservationTargetCommandResult>.Success(
                        new RecordGitHubObservationTargetCommandResult(command.RunId, source.ToView(), recorded))
                    : Result<RecordGitHubObservationTargetCommandResult>.Failure(GitHubObservationTargetErrors.Conflict());
        }

        var nowUtc = timeProvider.GetUtcNow();
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), source.RunId, source.ProjectId, source.OperationId, source.WorkspaceId, source.CommitSha, source.BranchName,
            command.Owner, command.Repository, nowUtc, Guid.NewGuid());
        var recordedEvent = RunEvent.Record(
            target.EventId,
            source.RunId,
            attemptId: null,
            RunEventType.GitHubObservationTargetRecorded,
            ParticipantIdentity.ForHuman(),
            GitHubObservationTargetEventPayload.Build(target),
            nowUtc);
        dbContext.GitHubObservationTargets.Add(target);
        dbContext.Events.Add(recordedEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await NotifyAsync(source.RunId, recordedEvent.Sequence, cancellationToken);

        return Result<RecordGitHubObservationTargetCommandResult>.Success(
            new RecordGitHubObservationTargetCommandResult(
                source.RunId,
                source.ToView(),
                new GitHubObservationTargetView(
                    target.Id, target.EventId, GitHubObservationTarget.Host, target.Owner, target.Repository, target.RecordedAtUtc)));
    }

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// target into a reported failure. Cancellation still propagates.</summary>
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
}
