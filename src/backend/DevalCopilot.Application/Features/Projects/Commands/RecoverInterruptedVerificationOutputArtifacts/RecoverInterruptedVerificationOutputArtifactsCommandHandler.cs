using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;

/// <summary>
/// Imports only deterministic sealed files owned by executions that are still Running at
/// startup. Partial files are deliberately deleted rather than promoted to final evidence;
/// the execution will subsequently be reconciled to Interrupted without fabricating a process
/// outcome.
/// </summary>
public sealed class RecoverInterruptedVerificationOutputArtifactsCommandHandler(
    IDevalCopilotDbContext dbContext,
    IVerificationOutputArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<RecoverInterruptedVerificationOutputArtifactsCommand, Result<int>>
{
    private static readonly VerificationOutputPurpose[] Purposes =
    [VerificationOutputPurpose.StandardOutput, VerificationOutputPurpose.StandardError];

    public async Task<Result<int>> HandleAsync(
        RecoverInterruptedVerificationOutputArtifactsCommand command,
        CancellationToken cancellationToken)
    {
        var executions = (await dbContext.VerificationExecutions
            .Where(execution => execution.Status == VerificationExecutionStatus.Running)
            .ToListAsync(cancellationToken))
            .OrderBy(execution => execution.ClaimedAtUtc)
            .ThenBy(execution => execution.Id)
            .ToArray();

        var importedCount = 0;
        foreach (var execution in executions)
        {
            foreach (var purpose in Purposes)
            {
                var sealedFile = await artifactStore.DescribeSealedFileAsync(execution.Id, purpose, cancellationToken);
                if (sealedFile is null)
                {
                    // A partial file belongs to this exact execution/purpose, but it was never
                    // sealed and therefore is not durable evidence. Do not expose or hash it as
                    // final output.
                    artifactStore.DeletePartialFile(execution.Id, purpose);
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
                    artifactStore.DeletePartialFile(execution.Id, purpose);
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

        return Result<int>.Success(importedCount);
    }
}
