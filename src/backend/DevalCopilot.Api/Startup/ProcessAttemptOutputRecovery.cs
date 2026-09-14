using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedProcessOutputArtifact;
using DevalCopilot.Application.Features.Runs.Queries.GetRunningProcessAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetTerminalProcessAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Startup;

/// <summary>
/// Runs once at host startup, before <c>ReconcileInterruptedProcessAttemptsCommand</c>: for
/// every Process attempt this instance finds still <c>Running</c> (about to be reconciled to
/// <c>Interrupted</c>), discovers whatever output the prior host session captured for it —
/// already sealed but never recorded, or still partial and safe to seal now, since the host is
/// definitively gone and was always the file's only writer — and imports it as truthful
/// <see cref="ArtifactCaptureOutcome.PartialHostInterrupted"/> evidence. Idempotent: safe to run
/// again on a later restart, including one that follows this same pass being interrupted
/// mid-way, because every import first checks whether the artifact already exists.
///
/// Afterward, sweeps the one narrow, documented cleanup this slice performs: a ".partial" file
/// left behind by an attempt that is already terminal through some other path can never
/// legitimately still be written to (only a <c>Running</c> attempt is ever actively captured
/// to) and nothing durable will ever reference it, so it is safe to delete.
/// </summary>
public static class ProcessAttemptOutputRecovery
{
    private static readonly ArtifactPurpose[] Purposes = [ArtifactPurpose.ProcessStandardOutput, ArtifactPurpose.ProcessStandardError];

    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();

        var runningAttempts = await mediator.SendAsync(new GetRunningProcessAttemptsQuery(), cancellationToken);
        foreach (var attempt in runningAttempts)
        {
            foreach (var purpose in Purposes)
            {
                await RecoverOneAsync(mediator, artifactStore, logger, attempt.RunId, attempt.AttemptId, purpose, cancellationToken);
            }
        }

        var terminalAttempts = await mediator.SendAsync(new GetTerminalProcessAttemptsQuery(), cancellationToken);
        foreach (var attempt in terminalAttempts)
        {
            foreach (var purpose in Purposes)
            {
                if (artifactStore.HasPartialFile(attempt.RunId, attempt.AttemptId, purpose))
                {
                    artifactStore.DeleteOrphanedPartialFile(attempt.RunId, attempt.AttemptId, purpose);
                }
            }
        }
    }

    private static async Task RecoverOneAsync(
        IApplicationMediator mediator,
        IArtifactStore artifactStore,
        ILogger logger,
        Guid runId,
        Guid attemptId,
        ArtifactPurpose purpose,
        CancellationToken cancellationToken)
    {
        SealedOutputFile? sealedFile;
        try
        {
            // Already sealed (a prior session finished sealing but crashed before recording it)
            // is described, never re-sealed — a sealed file is renamed exactly once, ever. A
            // still-partial file is safe to seal now precisely because the host that owned its
            // only write handle is gone; no other process ever had it open.
            sealedFile = artifactStore.HasSealedFile(runId, attemptId, purpose)
                ? await artifactStore.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken)
                : await artifactStore.SealAsync(runId, attemptId, purpose, cancellationToken);
        }
        catch (Exception)
        {
            logger.LogError(
                "process_attempt_output_recovery_seal_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            return;
        }

        if (sealedFile is null)
        {
            // Nothing was ever captured for this stream, or the seal itself failed — either
            // way, safely nothing to import.
            return;
        }

        try
        {
            var result = await mediator.SendAsync(
                new RecordInterruptedProcessOutputArtifactCommand(
                    runId, attemptId, purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash),
                cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "process_attempt_output_recovery_import_rejected AttemptId={AttemptId} Purpose={Purpose} ErrorCode={ErrorCode}",
                    attemptId, purpose, result.Errors[0].Code);
            }
        }
        catch (Exception)
        {
            logger.LogError(
                "process_attempt_output_recovery_import_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
        }
    }
}
