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

    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var artifactStore = scope.ServiceProvider.GetRequiredService<IArtifactStore>();

        var runningAttempts = await mediator.SendAsync(new GetRunningAgentAttemptsQuery(), cancellationToken);
        foreach (var attempt in runningAttempts)
        {
            foreach (var purpose in Purposes)
            {
                await RecoverOneAsync(mediator, artifactStore, logger, attempt.RunId, attempt.AttemptId, purpose, cancellationToken);
            }
        }

        var terminalAttempts = await mediator.SendAsync(new GetTerminalAgentAttemptsQuery(), cancellationToken);
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
            sealedFile = artifactStore.HasSealedFile(runId, attemptId, purpose)
                ? await artifactStore.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken)
                : await artifactStore.SealAsync(runId, attemptId, purpose, cancellationToken);
        }
        catch (Exception)
        {
            logger.LogError(
                "agent_attempt_output_recovery_seal_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
            return;
        }

        if (sealedFile is null)
        {
            return;
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
            }
        }
        catch (Exception)
        {
            logger.LogError(
                "agent_attempt_output_recovery_import_failed AttemptId={AttemptId} Purpose={Purpose}", attemptId, purpose);
        }
    }
}
