using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>
/// Records one proven outcome of a local-commit operation together with its run and workspace consequences and its sequenced event
/// in a single save (ADR-0029). Confirmed delivery completes the operation and the run; a definitely unpromoted failure fails the
/// run; a startup interruption proven unpromoted interrupts it; ambiguity changes neither the run nor invents a decision. The
/// operation status is an EF concurrency token, so two recorders can never both decide the same operation.
/// </summary>
internal sealed class LocalCommitOutcomeRecorder(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier)
{
    public Task<LocalCommitOperationView?> CompleteAsync(Guid operationId, bool sourceConsistent, CancellationToken cancellationToken) =>
        ApplyAsync(operationId, RunEventType.LocalCommitCompleted, (operation, run, workspace, now) =>
        {
            operation.Complete(now);
            if (run is { Lifecycle: RunLifecycle.Running })
            {
                run.Complete(now);
            }

            if (workspace is not null)
            {
                if (sourceConsistent && (workspace.Status == WorkspaceStatus.Committing || workspace.IsAttentionFromLocalCommit))
                {
                    workspace.FinishCommit();
                }
                else if (workspace.Status == WorkspaceStatus.Committing)
                {
                    workspace.MarkNeedsAttention(GitWorkspace.LocalCommitAttentionPrefix + "source_changed_after_commit");
                }
            }
        }, cancellationToken);

    public Task<LocalCommitOperationView?> FailAsync(Guid operationId, string reasonCode, CancellationToken cancellationToken) =>
        ApplyAsync(operationId, RunEventType.LocalCommitFailed, (operation, run, workspace, now) =>
        {
            operation.Fail(reasonCode, now);
            if (run is { Lifecycle: RunLifecycle.Running })
            {
                run.Fail(now);
            }

            ReleaseReservation(workspace);
        }, cancellationToken);

    public Task<LocalCommitOperationView?> InterruptAsync(Guid operationId, string reasonCode, CancellationToken cancellationToken) =>
        ApplyAsync(operationId, RunEventType.LocalCommitInterrupted, (operation, run, workspace, now) =>
        {
            operation.Interrupt(reasonCode, now);
            if (run is { Lifecycle: RunLifecycle.Running })
            {
                run.MarkInterrupted(now);
            }

            ReleaseReservation(workspace);
        }, cancellationToken);

    public Task<LocalCommitOperationView?> NeedsAttentionAsync(
        Guid operationId, string reasonCode, CancellationToken cancellationToken) =>
        ApplyAsync(operationId, RunEventType.LocalCommitNeedsAttention, (operation, _, workspace, _) =>
        {
            operation.MarkNeedsAttention(reasonCode);
            if (workspace is { Status: WorkspaceStatus.Committing })
            {
                workspace.MarkNeedsAttention(GitWorkspace.LocalCommitAttentionPrefix + "ambiguous");
            }
        }, cancellationToken);

    private static void ReleaseReservation(GitWorkspace? workspace)
    {
        if (workspace is not null && (workspace.Status == WorkspaceStatus.Committing || workspace.IsAttentionFromLocalCommit))
        {
            workspace.FinishCommit();
        }
    }

    private async Task<LocalCommitOperationView?> ApplyAsync(
        Guid operationId,
        string eventType,
        Action<LocalCommitOperation, Run?, GitWorkspace?, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.LocalCommitOperations.SingleOrDefaultAsync(
            candidate => candidate.Id == operationId, cancellationToken);
        if (operation is not null)
        {
            // A populated tracker is never trusted: the identity map can hand back an older copy of this very row.
            await dbContext.Entry(operation).ReloadAsync(cancellationToken);
        }

        if (operation is null || operation.IsTerminal)
        {
            return operation is null ? null : LocalCommitOperationView.From(operation);
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == operation.RunId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces.SingleOrDefaultAsync(
            candidate => candidate.Id == operation.GitWorkspaceId, cancellationToken);
        if (run is not null)
        {
            await dbContext.Entry(run).ReloadAsync(cancellationToken);
        }

        if (workspace is not null)
        {
            await dbContext.Entry(workspace).ReloadAsync(cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        apply(operation, run, workspace, now);

        var recorded = RunEvent.Record(
            Guid.NewGuid(),
            operation.RunId,
            attemptId: null,
            eventType,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new
            {
                operationId = operation.Id,
                status = operation.Status.ToString(),
                reason = operation.OutcomeReasonCode,
                commit = operation.Status == LocalCommitStatus.Completed ? operation.CommitSha : null,
            }),
            now);
        dbContext.Events.Add(recorded);
        await dbContext.SaveChangesAsync(cancellationToken);

        await LocalCommitNotification.NotifyAsync(eventNotifier, operation.RunId, recorded.Sequence, cancellationToken);

        return LocalCommitOperationView.From(operation);
    }
}
