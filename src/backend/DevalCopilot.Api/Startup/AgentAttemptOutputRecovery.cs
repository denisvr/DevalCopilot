using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedAgentArtifact;
using DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetTerminalAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Startup;

/// <summary>
/// Runs once at host startup, before <c>ReconcileInterruptedAgentAttemptsCommand</c>: for every
/// Agent attempt this instance finds still <c>Running</c> (about to be reconciled to
/// <c>Interrupted</c>), imports whatever stdout/stderr/final-response the prior host session
/// captured for it as truthful <see cref="ArtifactCaptureOutcome.PartialHostInterrupted"/>
/// evidence. Never touches the context-manifest artifact — that one is sealed at claim time,
/// before dispatch, and is never a candidate for this recovery path. Idempotent: every import
/// first checks whether the artifact already exists. Mirrors
/// <see cref="ProcessAttemptOutputRecovery"/> exactly, for the Agent-attempt equivalent.
/// </summary>
public static class AgentAttemptOutputRecovery
{
    private static readonly ArtifactPurpose[] Purposes =
    [
        ArtifactPurpose.AgentStandardOutput, ArtifactPurpose.AgentStandardError, ArtifactPurpose.AgentFinalResponse,
    ];

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

        var runningAttempts = await mediator.SendAsync(new GetRunningAgentAttemptsQuery(), cancellationToken);
        foreach (var attempt in runningAttempts)
        {
            foreach (var purpose in Purposes)
            {
                optionalOutputDeferred |= !await RecoverOneAsync(
                    mediator, artifactStore, logger, attempt.RunId, attempt.AttemptId, purpose, cancellationToken);
            }
        }

        var terminalAttempts = await mediator.SendAsync(new GetTerminalAgentAttemptsQuery(), cancellationToken);
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
            sealedFile = hadSealedFile
                ? await artifactStore.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken)
                : await artifactStore.SealAsync(runId, attemptId, purpose, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ADR-0072 (review WEP-R2): only a file-system fault on this one historical output item is optional; its files stay in
            // place. Any other exception is unclassified and escapes as fatal.
            logger.LogError(
                "agent_attempt_output_recovery_seal_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            return false;
        }

        if (sealedFile is null)
        {
            if (hadSealedFile || hadPartialFile)
            {
                logger.LogError(
                    "agent_attempt_output_recovery_unreadable AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
                return false;
            }

            // Nothing was ever captured for this stream: nothing to import.
            return true;
        }

        try
        {
            var result = await mediator.SendAsync(
                new RecordInterruptedAgentArtifactCommand(
                    runId, attemptId, purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash),
                cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "agent_attempt_output_recovery_import_rejected AttemptId={AttemptId} Purpose={Purpose} ErrorCode={ErrorCode}",
                    attemptId, purpose, result.Errors[0].Code);
                return false;
            }
        }
        catch (Exception)
        {
            logger.LogError(
                "agent_attempt_output_recovery_import_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            throw;
        }

        return true;
    }
}
