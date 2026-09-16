using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed verification recipes. No shell is involved, and a dispatch
/// marker is committed before starting the child process so a later polling cycle cannot launch
/// it twice. A host restart leaves any unrecorded execution for startup reconciliation.
/// </summary>
public sealed class VerificationExecutionSupervisor(
    IServiceScopeFactory scopeFactory,
    IProcessExecutionAdapter processAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IVerificationOutputArtifactStore artifactStore,
    ILogger<VerificationExecutionSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var executions = await DispatchAsync(new GetEligibleVerificationExecutionsQuery(), stoppingToken);
                foreach (var execution in executions)
                {
                    await ExecuteOneAsync(execution, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("verification_execution_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleVerificationExecution execution, CancellationToken stoppingToken)
    {
        var preDispatchEvidence = await evidenceReader.CaptureAsync(execution.WorkspacePath, stoppingToken);
        if (preDispatchEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && preDispatchEvidence.FingerprintSha256 is not null
            && !string.Equals(preDispatchEvidence.FingerprintSha256, execution.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            await DispatchAsync(
                new RecordVerificationExecutionSourceChangedCommand(
                    execution.VerificationExecutionId, preDispatchEvidence.FingerprintSha256!),
                CancellationToken.None);
            return;
        }

        if (preDispatchEvidence.Outcome != GitWorkspaceEvidenceOutcome.Success || preDispatchEvidence.FingerprintSha256 is null)
        {
            logger.LogError("verification_execution_pre_dispatch_evidence_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            return;
        }

        var dispatched = await DispatchAsync(
            new MarkVerificationExecutionDispatchedCommand(execution.VerificationExecutionId), stoppingToken);
        if (dispatched.IsFailure)
        {
            return;
        }

        ProcessExecutionResult result;
        try
        {
            result = await processAdapter.ExecuteAsync(new ProcessExecutionRequest
            {
                ExecutablePath = execution.ExecutablePath,
                Arguments = execution.Arguments,
                WorkingDirectory = execution.WorkspacePath,
                ApprovedRoot = execution.WorkspacePath,
                Timeout = TimeSpan.FromSeconds(execution.TimeoutSeconds),
                EnvironmentVariables = new Dictionary<string, string>(),
                StandardOutputSinkPath = artifactStore.GetPartialPath(execution.VerificationExecutionId, VerificationOutputPurpose.StandardOutput),
                StandardErrorSinkPath = artifactStore.GetPartialPath(execution.VerificationExecutionId, VerificationOutputPurpose.StandardError),
            }, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("verification_execution_process_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            artifactStore.DeletePartialFile(execution.VerificationExecutionId, VerificationOutputPurpose.StandardOutput);
            artifactStore.DeletePartialFile(execution.VerificationExecutionId, VerificationOutputPurpose.StandardError);
            await RecordResultAsync(
                execution,
                VerificationExecutionOutcome.Failed,
                null,
                false,
                false,
                false,
                CancellationToken.None);
            return;
        }

        var outcome = result.Outcome switch
        {
            ProcessExecutionOutcome.Exited => VerificationExecutionOutcome.Exited,
            ProcessExecutionOutcome.TimedOut => VerificationExecutionOutcome.TimedOut,
            ProcessExecutionOutcome.Cancelled => VerificationExecutionOutcome.Cancelled,
            _ => throw new ArgumentOutOfRangeException(),
        };
        await RecordResultAsync(
            execution,
            outcome,
            result.ExitCode,
            true,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleVerificationExecution execution,
        VerificationExecutionOutcome outcome,
        int? exitCode,
        bool captureOutputArtifacts,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        CancellationToken cancellationToken)
    {
        var evidence = await evidenceReader.CaptureAsync(execution.WorkspacePath, CancellationToken.None);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 is null)
        {
            logger.LogError("verification_execution_completion_evidence_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            return;
        }

        var sealedArtifacts = captureOutputArtifacts
            ? await SealArtifactsAsync(execution.VerificationExecutionId, standardOutputTruncated, standardErrorTruncated)
            : [];
        await DispatchAsync(
            new RecordVerificationExecutionResultCommand(
                execution.VerificationExecutionId,
                outcome,
                exitCode,
                evidence.FingerprintSha256,
                sealedArtifacts),
            cancellationToken);
    }

    private async Task<IReadOnlyList<SealedVerificationOutputArtifact>> SealArtifactsAsync(
        Guid verificationExecutionId, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedVerificationOutputArtifact>();
        foreach (var (purpose, truncated) in new[]
                 {
                     (VerificationOutputPurpose.StandardOutput, standardOutputTruncated),
                     (VerificationOutputPurpose.StandardError, standardErrorTruncated),
                 })
        {
            var sealedFile = await artifactStore.SealAsync(verificationExecutionId, purpose, CancellationToken.None);
            if (sealedFile is not null)
            {
                results.Add(new SealedVerificationOutputArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, cancellationToken);
    }

    private async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(query, cancellationToken);
    }
}
