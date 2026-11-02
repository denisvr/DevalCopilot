using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;

/// <summary>
/// Imports only deterministic sealed files owned by executions that are still Running at
/// startup. A partial file is never promoted to final evidence; the execution will subsequently
/// be reconciled to Interrupted without fabricating a process outcome.
///
/// ADR-0072 (review WEP-R2): a partial file is removed only when an artifact of the same purpose is recorded for that execution
/// (independently proved redundant); an unsealed, unrecorded partial is preserved for every later launch. A file-system fault on
/// one historical output item (reading or hashing its sealed file) is an optional-output condition: the item is skipped, its files
/// stay in place and <see cref="RecoverInterruptedVerificationOutputArtifactsCommandResult.OptionalOutputDeferred"/> is reported.
/// Every database query and commit stays outside that handling, and any other exception escapes, so both remain fatal at startup.
/// </summary>
public sealed class RecoverInterruptedVerificationOutputArtifactsCommandHandler(
    IDevalCopilotDbContext dbContext,
    IVerificationOutputArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<RecoverInterruptedVerificationOutputArtifactsCommand, Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>>
{
    private static readonly VerificationOutputPurpose[] Purposes =
    [VerificationOutputPurpose.StandardOutput, VerificationOutputPurpose.StandardError];

    public async Task<Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>> HandleAsync(
        RecoverInterruptedVerificationOutputArtifactsCommand command,
        CancellationToken cancellationToken)
    {
        // ADR-0060: an execution linked to a local execution sequence is closed or quarantined by the sequence's own startup command.
        var executions = (await dbContext.VerificationExecutions
            .Where(execution => execution.Status == VerificationExecutionStatus.Running
                && !dbContext.LocalExecutionSequenceSteps.Any(step => step.OperationId == execution.Id))
            .ToListAsync(cancellationToken))
            .OrderBy(execution => execution.ClaimedAtUtc)
            .ThenBy(execution => execution.Id)
            .ToArray();

        var importedCount = 0;
        var optionalOutputDeferred = false;
        foreach (var execution in executions)
        {
            foreach (var purpose in Purposes)
            {
                SealedVerificationOutputFile? sealedFile;
                try
                {
                    sealedFile = await artifactStore.DescribeSealedFileAsync(execution.Id, purpose, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    sealedFile = null;
                }

                if (sealedFile is null && artifactStore.HasSealedFile(execution.Id, purpose))
                {
                    // One historical output file exists but could not be read: skipped and left in place, never a reason to stop
                    // startup, and reported as deferred optional output.
                    optionalOutputDeferred = true;
                    continue;
                }

                // Re-describe/hash validation happens before this idempotency check on every
                // recovery pass. The deterministic path and execution key keep another
                // execution or project from supplying metadata for this one.
                var alreadyImported = await dbContext.VerificationOutputArtifacts
                    .AnyAsync(artifact =>
                        artifact.VerificationExecutionId == execution.Id && artifact.Purpose == purpose,
                        cancellationToken);
                if (alreadyImported)
                {
                    // An artifact of this purpose is recorded, so a leftover partial is proved redundant.
                    artifactStore.DeletePartialFile(execution.Id, purpose);
                    continue;
                }

                if (sealedFile is null)
                {
                    // A partial file belongs to this exact execution/purpose, but it was never sealed and therefore is not durable
                    // evidence: it is neither exposed, hashed nor deleted, and stays for every later launch.
                    continue;
                }

                dbContext.VerificationOutputArtifacts.Add(VerificationOutputArtifact.Record(
                    Guid.NewGuid(),
                    execution.Id,
                    purpose,
                    sealedFile.RelativeStoragePath,
                    sealedFile.ContentHash,
                    sealedFile.ByteLength,
                    truncated: null,
                    captureOutcome: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption,
                    timeProvider.GetUtcNow()));
                await dbContext.SaveChangesAsync(cancellationToken);
                importedCount++;
                artifactStore.DeletePartialFile(execution.Id, purpose);
            }
        }

        return Result<RecoverInterruptedVerificationOutputArtifactsCommandResult>.Success(new(importedCount, optionalOutputDeferred));
    }
}
