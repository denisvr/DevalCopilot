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
/// left behind by an attempt that is already terminal is deleted only when that attempt already
/// recorded an artifact of the same purpose, so the partial is independently proved redundant
/// (ADR-0072); every other terminal partial is preserved.
/// </summary>
public static class ProcessAttemptOutputRecovery
{
    private static readonly ArtifactPurpose[] Purposes = [ArtifactPurpose.ProcessStandardOutput, ArtifactPurpose.ProcessStandardError];

    /// <remarks>ADR-0072: a per-item file-system seal failure or rejected import is an optional-output warning and leaves the files
    /// in place; a database fault from a query or an import, or any unclassified exception, escapes as fatal. A terminal attempt's partial file is removed only when that attempt
    /// already recorded an artifact of the same purpose (independently proved redundant); any other partial, including one left by a
    /// deferred import on an earlier launch or by a writer that was never proved closed, is preserved.</remarks>
    public static async Task<OutputRecoveryOutcome> RunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        var optionalOutputDeferred = false;
        var redundantCleanupDeferred = false;
        await using var scope = services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();

        var runningAttempts = await mediator.SendAsync(new GetRunningProcessAttemptsQuery(), cancellationToken);
        foreach (var attempt in runningAttempts)
        {
            foreach (var purpose in Purposes)
            {
                optionalOutputDeferred |= !await RecoverOneAsync(
                    mediator, artifactStore, logger, attempt.RunId, attempt.AttemptId, purpose, cancellationToken);
            }
        }

        var terminalAttempts = await mediator.SendAsync(new GetTerminalProcessAttemptsQuery(), cancellationToken);
        foreach (var attempt in terminalAttempts)
        {
            foreach (var purpose in Purposes)
            {
                if (artifactStore.HasPartialFile(attempt.RunId, attempt.AttemptId, purpose)
                    && attempt.RecordedPurposes?.Contains(purpose) == true)
                {
                    artifactStore.DeleteOrphanedPartialFile(attempt.RunId, attempt.AttemptId, purpose);
                    redundantCleanupDeferred |= artifactStore.HasPartialFile(attempt.RunId, attempt.AttemptId, purpose);
                }
            }
        }

        return new OutputRecoveryOutcome(optionalOutputDeferred, redundantCleanupDeferred);
    }

    /// <summary>Returns false when this item's output could not be sealed or its import was rejected (an optional-output
    /// warning); a database fault propagates.</summary>
    private static async Task<bool> RecoverOneAsync(
        IApplicationMediator mediator,
        IArtifactStore artifactStore,
        ILogger logger,
        Guid runId,
        Guid attemptId,
        ArtifactPurpose purpose,
        CancellationToken cancellationToken)
    {
        // ADR-0072 (review WEP-R2): existence is read first, so a file that exists but cannot be described or sealed is a deferred
        // optional output (its files stay in place), never mistaken for an absence.
        var hadSealedFile = artifactStore.HasSealedFile(runId, attemptId, purpose);
        var hadPartialFile = !hadSealedFile && artifactStore.HasPartialFile(runId, attemptId, purpose);
        SealedOutputFile? sealedFile;
        try
        {
            // Already sealed (a prior session finished sealing but crashed before recording it)
            // is described, never re-sealed — a sealed file is renamed exactly once, ever. A
            // still-partial file is safe to seal now precisely because the host that owned its
            // only write handle is gone; no other process ever had it open.
            sealedFile = hadSealedFile
                ? await artifactStore.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken)
                : await artifactStore.SealAsync(runId, attemptId, purpose, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ADR-0072 (review WEP-R2): only a file-system fault on this one historical output item is optional; its files stay in
            // place. Any other exception is unclassified and escapes as fatal.
            logger.LogError(
                "process_attempt_output_recovery_seal_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            return false;
        }

        if (sealedFile is null)
        {
            if (hadSealedFile || hadPartialFile)
            {
                logger.LogError(
                    "process_attempt_output_recovery_unreadable AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
                return false;
            }

            // Nothing was ever captured for this stream: nothing to import.
            return true;
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
                return false;
            }
        }
        catch (Exception)
        {
            logger.LogError(
                "process_attempt_output_recovery_import_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            throw;
        }

        return true;
    }
}
