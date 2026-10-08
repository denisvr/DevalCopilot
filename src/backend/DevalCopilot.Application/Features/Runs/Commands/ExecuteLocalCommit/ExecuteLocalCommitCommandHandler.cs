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

namespace DevalCopilot.Application.Features.Runs.Commands.ExecuteLocalCommit;

public sealed class ExecuteLocalCommitCommandHandler(
    IDevalCopilotDbContext dbContext,
    ILocalCommitRepository repository,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ExecuteLocalCommitCommand, Result<LocalCommitOperationView>>
{
    public async Task<Result<LocalCommitOperationView>> HandleAsync(
        ExecuteLocalCommitCommand command, CancellationToken cancellationToken)
    {
        var operation = await dbContext.LocalCommitOperations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.OperationId, cancellationToken);
        if (operation is null)
        {
            return Result<LocalCommitOperationView>.Failure(LocalCommitErrors.OperationNotFound());
        }

        if (operation.Status != LocalCommitStatus.Prepared)
        {
            // Single use: an operation that already left Prepared is never executed again, whatever its outcome.
            return Result<LocalCommitOperationView>.Success(LocalCommitOperationView.From(operation));
        }

        var recorder = new LocalCommitOutcomeRecorder(dbContext, timeProvider, eventNotifier);
        var facts = await LocalCommitFactsReader.ReadAsync(dbContext, operation, cancellationToken);
        if (facts is null)
        {
            return Result<LocalCommitOperationView>.Success(
                (await recorder.NeedsAttentionAsync(operation.Id, "local_commit.ownership_unprovable", cancellationToken))!);
        }

        var marked = await TryMarkExecutingAsync(operation, cancellationToken);
        if (marked.Refusal is { } refusal)
        {
            // Authority refusal is a database fact, not proof that a previous process did not move Git.  Release the durable
            // reservation only after the adapter proves the recorded parent/index/ownership state; otherwise leave the operation
            // and workspace visibly reserved for recovery.
            return await CleanedAsync(
                await RecordNotPromotedOrAttentionAsync(recorder, operation, facts, refusal, cancellationToken), facts);
        }

        if (!marked.Marked)
        {
            var current = await dbContext.LocalCommitOperations.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == operation.Id, cancellationToken);
            return Result<LocalCommitOperationView>.Success(LocalCommitOperationView.From(current));
        }

        var acquisition = await repository.AcquireIndexEffectsAsync(facts, cancellationToken);
        if (acquisition is null)
        {
            // Nothing was mutated, but that is only a claim until the exact parent, index, lock and ownership are read back: a
            // foreign lock or an external change keeps the reservation and the nonterminal operation.
            return await CleanedAsync(
                await RecordNotPromotedOrAttentionAsync(
                    recorder, operation, facts, "local_commit.index_acquisition_unproven", CancellationToken.None),
                facts);
        }

        try
        {
            if (!await RecordIndexAcquisitionAsync(operation.Id, acquisition, cancellationToken))
            {
                return Result<LocalCommitOperationView>.Success(
                    (await recorder.NeedsAttentionAsync(operation.Id, "local_commit.index_receipt_unproven", CancellationToken.None))!);
            }
        }
        catch (Exception)
        {
            // The live handles intentionally remain held until process loss. A failed receipt must not permit the ref mutation or
            // an ordinary cleanup; startup sees any surviving lock as unknown and keeps the durable reservation.
            return Result<LocalCommitOperationView>.Failure(Error.Failure(
                "local_commit.acquisition_not_recorded", "The local commit acquisition could not be recorded; it will be reconciled."));
        }

        LocalCommitExecutionResult result;
        try
        {
            result = await repository.PromoteRefAsync(facts, acquisition, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown mid-execution: leave the marker as written; startup recovery proves the outcome.
            throw;
        }
        catch (Exception)
        {
            result = new LocalCommitExecutionResult(LocalCommitExecutionOutcome.Ambiguous, "local_commit.execution_error", false);
        }

        if (result.Outcome == LocalCommitExecutionOutcome.Promoted)
        {
            var quarantineName = $"devalcopilot-{operation.Id:N}.index-preimage";
            try
            {
                if (!await RecordIndexReplacementPlanAsync(operation.Id, quarantineName, cancellationToken))
                {
                    return Result<LocalCommitOperationView>.Success(
                        (await recorder.NeedsAttentionAsync(operation.Id, "local_commit.replacement_receipt_unproven", CancellationToken.None))!);
                }
            }
            catch (Exception)
            {
                return Result<LocalCommitOperationView>.Failure(Error.Failure(
                    "local_commit.replacement_not_recorded", "The local commit replacement could not be recorded; it will be reconciled."));
            }

            result = await repository.PromoteHeldIndexAsync(facts, acquisition, quarantineName, cancellationToken);
        }
        else if (result.Outcome == LocalCommitExecutionOutcome.NotPromoted
            && !await repository.ReleaseHeldIndexEffectsAsync(facts, acquisition, CancellationToken.None))
        {
            result = new LocalCommitExecutionResult(LocalCommitExecutionOutcome.Ambiguous, "local_commit.lock_release_unproven", false);
        }

        try
        {
            var view = result.Outcome switch
            {
                LocalCommitExecutionOutcome.Promoted =>
                    await recorder.CompleteAsync(operation.Id, result.SourceConsistent, CancellationToken.None),
                LocalCommitExecutionOutcome.NotPromoted =>
                    await RecordNotPromotedOrAttentionAsync(recorder, operation, facts, result.ReasonCode, CancellationToken.None),
                _ => await recorder.NeedsAttentionAsync(operation.Id, result.ReasonCode, CancellationToken.None),
            };
            return await CleanedAsync(view, facts);
        }
        catch (Exception)
        {
            // A persistence failure after the host mutation must fail closed: the durable state stays Executing with the
            // workspace reserved, and nothing here retries Git or invents an outcome. Startup recovery proves it.
            return Result<LocalCommitOperationView>.Failure(Error.Failure(
                "local_commit.outcome_not_recorded", "The local commit outcome could not be recorded; it will be reconciled."));
        }
    }

    /// <summary>An unproven result is recorded as NeedsAttention and keeps its preparation artifacts for startup recovery; removing
    /// them would destroy evidence while the workspace remains reserved. Only a recorded Completed or Failed outcome makes the
    /// artifacts inert, and a cleanup fault then changes nothing recorded.</summary>
    private async Task<Result<LocalCommitOperationView>> CleanedAsync(LocalCommitOperationView? view, LocalCommitFacts facts)
    {
        if (view is { Status: nameof(LocalCommitStatus.Completed) or nameof(LocalCommitStatus.Failed) })
        {
            try
            {
                await repository.CleanupAsync(facts, removeOwnedLock: false, CancellationToken.None);
            }
            catch (Exception)
            {
                // The prepared artifact is inert once the outcome is recorded; recovery or a later cleanup removes it.
            }
        }

        return Result<LocalCommitOperationView>.Success(view!);
    }

    private static bool IsSafeUnpromoted(LocalCommitFacts facts, LocalCommitInspection inspection) =>
        inspection.Outcome == LocalCommitInspectionOutcome.Observed
        && inspection.OwnershipProven
        && inspection.HeadBoundToBranch
        && string.Equals(inspection.BranchTipSha, facts.ParentCommitSha, StringComparison.Ordinal)
        && inspection.CommitObject == LocalCommitObjectState.ExactMatch
        && inspection.Index == LocalCommitIndexState.Preimage
        && inspection.IndexLock == LocalCommitLockState.None
        && inspection.ReferenceLocks == LocalCommitReferenceLockState.Clear;

    private async Task<LocalCommitOperationView?> RecordNotPromotedOrAttentionAsync(
        LocalCommitOutcomeRecorder recorder,
        LocalCommitOperation operation,
        LocalCommitFacts facts,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        LocalCommitInspection inspection;
        try
        {
            inspection = await repository.InspectAsync(facts, cancellationToken);
        }
        catch (Exception)
        {
            return await recorder.NeedsAttentionAsync(operation.Id, "local_commit.release_unproven", cancellationToken);
        }

        return IsSafeUnpromoted(facts, inspection)
            ? await recorder.FailAsync(operation.Id, reasonCode, cancellationToken)
            : await recorder.NeedsAttentionAsync(operation.Id, "local_commit.release_unproven", cancellationToken);
    }

    private async Task<bool> RecordIndexAcquisitionAsync(
        Guid operationId, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var operation = await dbContext.LocalCommitOperations.SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken);
        if (operation is not null)
        {
            await dbContext.Entry(operation).ReloadAsync(cancellationToken);
        }

        if (operation is null || operation.Status != LocalCommitStatus.Executing)
        {
            return false;
        }

        operation.RecordIndexAcquisition(
            acquisition.AdministrativeDirectoryIdentity,
            acquisition.PreimageIdentity,
            acquisition.PreimageLength,
            acquisition.PreparedArtifactIdentity,
            acquisition.PreparedArtifactLength,
            acquisition.LockIdentity,
            acquisition.LockLength,
            timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> RecordIndexReplacementPlanAsync(
        Guid operationId, string quarantineName, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var operation = await dbContext.LocalCommitOperations.SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken);
        if (operation is not null)
        {
            await dbContext.Entry(operation).ReloadAsync(cancellationToken);
        }

        if (operation is null || operation.Status != LocalCommitStatus.Executing)
        {
            return false;
        }

        operation.PlanIndexReplacement(quarantineName, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private sealed record Marking(bool Marked, string? Refusal);

    /// <summary>One write-locked transaction: the operation moves Prepared to Executing (the single-use marker) only after the
    /// complete authority is read again and equals the pinned identity. A refusal rolls everything back.</summary>
    private async Task<Marking> TryMarkExecutingAsync(LocalCommitOperation operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();

        var marked = await dbContext.LocalCommitOperations
            .Where(candidate => candidate.Id == operation.Id && candidate.Status == LocalCommitStatus.Prepared)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, LocalCommitStatus.Executing)
                    .SetProperty(candidate => candidate.ExecutionStartedAtUtc, nowUtc),
                cancellationToken);
        if (marked != 1)
        {
            return new Marking(false, null);
        }

        var reader = new LocalCommitAuthorityReader(dbContext);
        var selection = new LocalCommitAuthorityReader.Selection(
            operation.GitCheckpointId, operation.CodeReviewAttemptId, operation.HumanCheckpointReviewId);
        var read = await reader.ReadAsync(operation.RunId, selection, WorkspaceStatus.Committing, operation.Id, cancellationToken);
        if (read.Authority is not { } authority
            || !string.Equals(authority.AuthoritySha256, operation.AuthoritySha256, StringComparison.Ordinal)
            || authority.Workspace.Id != operation.GitWorkspaceId || authority.Lease.Id != operation.RepositoryMutationLeaseId
            || !string.Equals(authority.ExpectedParentCommitSha, operation.ParentCommitSha, StringComparison.Ordinal))
        {
            return new Marking(false, "local_commit.authority_changed");
        }

        var executing = RunEvent.Record(
            Guid.NewGuid(),
            operation.RunId,
            attemptId: null,
            RunEventType.LocalCommitExecuting,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { operationId = operation.Id }),
            nowUtc);
        dbContext.Events.Add(executing);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await LocalCommitNotification.NotifyAsync(eventNotifier, operation.RunId, executing.Sequence, cancellationToken);

        return new Marking(true, null);
    }
}
