using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;

/// <summary>
/// <see cref="Devalente.Shared.Cqrs.IManualTransactionCommand{TResult}"/> so Git inspection never
/// runs inside an ambient EF transaction, but this handler deliberately never accumulates every
/// attempt's transition into one final <c>SaveChangesAsync</c> either: each attempt's own
/// Interrupt/NeedsAttention decision is committed in its own short save boundary, immediately
/// after that attempt's evidence capture, before the next attempt is even considered. A later
/// attempt's evidence-reader exception (or, in principle, any other failure) can therefore never
/// roll back or leave unpersisted a transition this handler already durably decided for an
/// earlier attempt in the same pass.
/// </summary>
public sealed class ReconcileInterruptedImplementationAttemptsCommandHandler(
    IDevalCopilotDbContext dbContext, IGitWorkspaceEvidenceReader evidenceReader, TimeProvider timeProvider)
    : ICommandHandler<ReconcileInterruptedImplementationAttemptsCommand, Result<int>>
{
    private const string ImplementationOutcomeAmbiguousReasonCode = "workspaces.implementation_outcome_ambiguous";

    public async Task<Result<int>> HandleAsync(ReconcileInterruptedImplementationAttemptsCommand command, CancellationToken cancellationToken)
    {
        var interruptedAttemptIds = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.AgentRole == AgentRole.Implementer && attempt.Status == AttemptStatus.Running)
            .Select(attempt => attempt.Id)
            .ToListAsync(cancellationToken);

        if (interruptedAttemptIds.Count == 0)
        {
            return Result<int>.Success(0);
        }

        // Phase 1 — pure inventory/ownership consistency check, no-tracking, no mutation: a
        // single inconsistency across any affected attempt fails the whole pass closed before
        // anything is ever touched, exactly as a bulk validate-then-mutate pass would, but
        // without holding tracked entities across the mutation phase below.
        foreach (var attemptId in interruptedAttemptIds)
        {
            var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId, cancellationToken);

            var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == attempt.RunId, cancellationToken);
            if (run is null || run.Lifecycle != RunLifecycle.Running)
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.inconsistent_run_state", $"Implementation attempt {attemptId} is Running but its run is not consistently Running."));
            }

            var workspaceExists = await dbContext.GitWorkspaces
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
            if (!workspaceExists)
            {
                return Result<int>.Failure(Error.Conflict(
                    "attempts.orphaned_workspace", $"Implementation attempt {attemptId} has no owning workspace."));
            }
        }

        // Phase 2 — one attempt at a time: reload it, its run, and its workspace fresh
        // (untainted by any earlier iteration's in-memory state), capture Git evidence outside
        // any EF transaction when it is needed, decide and apply that one attempt's transition,
        // and commit it immediately.
        var reconciledCount = 0;
        foreach (var attemptId in interruptedAttemptIds)
        {
            await ReconcileOneAsync(attemptId, cancellationToken);
            reconciledCount++;
        }

        return Result<int>.Success(reconciledCount);
    }

    private async Task ReconcileOneAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleAsync(candidate => candidate.Id == attemptId, cancellationToken);
        if (attempt.Status != AttemptStatus.Running)
        {
            // Already reconciled — never possible for a distinct attempt id today, but never
            // assumed: an attempt this pass already committed a transition for is never
            // re-interrupted.
            return;
        }

        var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == attempt.RunId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();

        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            // Never dispatched: the provider was never invoked, so the worktree cannot have
            // been mutated by this attempt. Safe to just interrupt it — no evidence capture
            // needed, and the workspace is never flagged from this branch.
            attempt.Interrupt(nowUtc);
            run.MarkInterrupted(nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var mutationSuspected = false;
        if (workspace.Status == WorkspaceStatus.Ready)
        {
            // Dispatched, and the host was lost before a result could ever be recorded — the
            // worktree may have been mutated in the meantime. Independently re-read fresh Git
            // evidence outside any EF transaction; never assumed safe, and never trusted from
            // any value already tracked in memory. A non-cancellation exception here is treated
            // as evidence being unavailable, never as a reason to fail this entire startup pass.
            var evidence = await CaptureEvidenceSafelyAsync(workspace.WorkspacePath, cancellationToken);
            mutationSuspected = evidence is null
                || evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
                || !string.Equals(evidence.FingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal);
        }

        // If the workspace is not Ready, it is already flagged (or otherwise not current) for
        // an independent reason — never re-derive or overwrite that existing state from this
        // reconciliation pass, and never treated as an ambiguous mutation of this attempt's own.
        attempt.Interrupt(nowUtc);
        run.MarkInterrupted(nowUtc);
        if (mutationSuspected && workspace.Status == WorkspaceStatus.Ready)
        {
            workspace.MarkNeedsAttention(ImplementationOutcomeAmbiguousReasonCode);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mirrors <c>ImplementationSupervisor.CaptureEvidenceSafelyAsync</c>'s own
    /// reasoning: an evidence-reader exception is real, untrusted-source failure, never a reason
    /// to let this whole reconciliation pass die and leave every other interrupted attempt
    /// unreconciled at next startup too. Cancellation is the one exception that must still
    /// propagate honestly.</summary>
    private async Task<GitWorkspaceEvidenceResult?> CaptureEvidenceSafelyAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceReader.CaptureAsync(workspacePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
