using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecoverLocalCommitOperations;

public sealed class RecoverLocalCommitOperationsCommandHandler(
    IDevalCopilotDbContext dbContext,
    ILocalCommitRepository repository,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<RecoverLocalCommitOperationsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(RecoverLocalCommitOperationsCommand command, CancellationToken cancellationToken)
    {
        var open = await dbContext.LocalCommitOperations.AsNoTracking()
            .Where(operation => operation.Status != LocalCommitStatus.Completed
                && operation.Status != LocalCommitStatus.Failed
                && operation.Status != LocalCommitStatus.Interrupted)
            .ToListAsync(cancellationToken);

        open = open.OrderBy(operation => operation.CreatedAtUtc).ToList();
        var recorder = new LocalCommitOutcomeRecorder(dbContext, timeProvider, eventNotifier);
        var decided = 0;
        foreach (var operation in open)
        {
            var facts = await LocalCommitFactsReader.ReadAsync(dbContext, operation, cancellationToken);
            if (facts is null)
            {
                await AttentionAsync(recorder, operation, "local_commit.ownership_unprovable", cancellationToken);
                continue;
            }

            var inspection = await repository.InspectAsync(facts, cancellationToken);
            var decision = LocalCommitRecoveryPolicy.Decide(operation, inspection);
            switch (decision.Action)
            {
                case LocalCommitRecoveryAction.Interrupt:
                    if (decision.RemoveOwnedLock && !await repository.CleanupAsync(facts, removeOwnedLock: true, cancellationToken))
                    {
                        await AttentionAsync(recorder, operation, "local_commit.lock_cleanup_unproven", cancellationToken);
                        break;
                    }

                    await recorder.InterruptAsync(operation.Id, decision.ReasonCode, cancellationToken);
                    await repository.CleanupAsync(facts, removeOwnedLock: false, cancellationToken);
                    decided++;
                    break;

                case LocalCommitRecoveryAction.Complete:
                    if (decision.RemoveOwnedLock && !await repository.CleanupAsync(facts, removeOwnedLock: true, cancellationToken))
                    {
                        await AttentionAsync(recorder, operation, "local_commit.lock_cleanup_unproven", cancellationToken);
                        break;
                    }

                    await recorder.CompleteAsync(operation.Id, inspection.SourceConsistent, cancellationToken);
                    await repository.CleanupAsync(facts, removeOwnedLock: false, cancellationToken);
                    decided++;
                    break;

                case LocalCommitRecoveryAction.FinishPromotionThenComplete:
                    if (operation.IndexQuarantineName is null)
                    {
                        await AttentionAsync(recorder, operation, "local_commit.replacement_receipt_unproven", cancellationToken);
                        break;
                    }

                    var acquisition = await repository.AcquirePendingIndexEffectsAsync(
                        facts, operation.IndexQuarantineName, cancellationToken);
                    if (acquisition is null || !await RecordRecoveryAcquisitionAsync(operation.Id, acquisition, cancellationToken))
                    {
                        await AttentionAsync(recorder, operation, "local_commit.recovery_acquisition_unproven", cancellationToken);
                        break;
                    }

                    var finished = await repository.PromoteHeldIndexAsync(
                        facts, acquisition, operation.IndexQuarantineName, cancellationToken);
                    if (finished.Outcome != LocalCommitExecutionOutcome.Promoted)
                    {
                        await AttentionAsync(recorder, operation, finished.ReasonCode, cancellationToken);
                        break;
                    }

                    await recorder.CompleteAsync(operation.Id, finished.SourceConsistent, cancellationToken);
                    await repository.CleanupAsync(facts, removeOwnedLock: false, cancellationToken);
                    decided++;
                    break;

                default:
                    await AttentionAsync(recorder, operation, decision.ReasonCode, cancellationToken);
                    break;
            }
        }

        return Result<int>.Success(decided);
    }

    private static async Task AttentionAsync(
        LocalCommitOutcomeRecorder recorder, LocalCommitOperation operation, string reasonCode, CancellationToken cancellationToken)
    {
        // An operation already flagged for the same reason is left exactly as found: restarting must not append the same fact.
        if (operation.Status == LocalCommitStatus.NeedsAttention && operation.OutcomeReasonCode == reasonCode)
        {
            return;
        }

        await recorder.NeedsAttentionAsync(operation.Id, reasonCode, cancellationToken);
    }

    private async Task<bool> RecordRecoveryAcquisitionAsync(
        Guid operationId, LocalCommitIndexAcquisition acquisition, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var operation = await dbContext.LocalCommitOperations.SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken);
        if (operation is not null)
        {
            await dbContext.Entry(operation).ReloadAsync(cancellationToken);
        }

        if (operation is null)
        {
            return false;
        }

        operation.RenewIndexAcquisitionForRecovery(
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
}
