using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.RecordLocalExecutionSequenceVerificationDispatchRefusal;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed verification recipes. No shell is involved, and a dispatch
/// marker is committed before starting the child process so a later polling cycle cannot launch
/// it twice. The eligibility feed only proposes candidates: the single-use marker is committed by a fresh decision that re-reads
/// the durable execution and its ownership and requires agreement with the snapshot carried from the feed, so a refused decision
/// launches nothing and leaves the execution pending. A host restart leaves any unrecorded execution for startup reconciliation.
/// The filesystem is not frozen between the physical observation, the marker commit and the process start.
///
/// ADR-0072: after the dispatch commit, every execution receives the one fixed Windows verification environment profile from
/// <see cref="IVerificationEnvironmentProvider"/>; an unavailable prerequisite completes the execution as Failed with
/// <see cref="VerificationExecutionDiagnostic.EnvironmentUnavailable"/> and no process, instead of leaving it pending. The result
/// records the profile version and the adapter's I/O completion facts: an exit code zero with incomplete I/O is recorded unchanged and
/// fails. A stream whose writer was not proved closed is never sealed, and the owned scratch is removed only after a proved exit with
/// complete I/O.
/// </summary>
public sealed class VerificationExecutionSupervisor(
    IServiceScopeFactory scopeFactory,
    IProcessExecutionAdapter processAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IVerificationOutputArtifactStore artifactStore,
    IVerificationEnvironmentProvider environmentProvider,
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
                    execution.VerificationExecutionId, preDispatchEvidence.FingerprintSha256!, execution.ToSnapshot()),
                CancellationToken.None);
            return;
        }

        if (preDispatchEvidence.Outcome != GitWorkspaceEvidenceOutcome.Success || preDispatchEvidence.FingerprintSha256 is null)
        {
            logger.LogError("verification_execution_pre_dispatch_evidence_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            return;
        }

        var dispatched = await DispatchAsync(
            new MarkVerificationExecutionDispatchedCommand(execution.VerificationExecutionId, execution.ToSnapshot()), stoppingToken);
        if (dispatched.IsFailure)
        {
            if (dispatched.Errors[0].Code == MarkVerificationExecutionDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode)
            {
                // A linked, still-undispatched execution whose sequence authority ended is closed by its dedicated refusal (ADR-0060)
                // instead of staying pending: the recorder re-proves the link and the refusal under its own write lock.
                await DispatchAsync(
                    new RecordLocalExecutionSequenceVerificationDispatchRefusalCommand(execution.ProjectId, execution.VerificationExecutionId),
                    CancellationToken.None);
            }

            return;
        }

        // ADR-0065: the original dispatch commit succeeded, so this host alone holds the opaque in-memory ticket of a linked local
        // execution verification (null for every unlinked execution). It is carried unchanged to the ordinary result recorder.
        var ticket = dispatched.Value.Ticket;

        // ADR-0072 (review WEP-R5): the dispatch marker is already durable, so a preparation that throws is recorded like an
        // unavailable environment — once, terminal, with the closed diagnostic and no process — instead of escaping the iteration and
        // leaving the execution Running. No profile was prepared, so none is recorded; no process ran, so the execution's own scratch
        // is released. Nothing is redispatched.
        VerificationEnvironmentPreparation environment;
        try
        {
            environment = environmentProvider.Prepare(execution.VerificationExecutionId, execution.ExecutablePath, execution.Arguments);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("verification_execution_environment_preparation_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            environmentProvider.ReleaseScratch(execution.VerificationExecutionId);
            await RecordResultAsync(
                execution,
                ticket,
                VerificationExecutionOutcome.Failed,
                null,
                false,
                false,
                false,
                null,
                CancellationToken.None,
                null,
                VerificationExecutionDiagnostic.EnvironmentUnavailable);
            return;
        }

        if (!environment.IsAvailable)
        {
            logger.LogError("verification_execution_environment_unavailable ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            await RecordResultAsync(
                execution,
                ticket,
                VerificationExecutionOutcome.Failed,
                null,
                false,
                false,
                false,
                null,
                CancellationToken.None,
                environment.ProfileVersion,
                VerificationExecutionDiagnostic.EnvironmentUnavailable);
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
                EnvironmentVariables = environment.EnvironmentVariables,
                StandardOutputSinkPath = artifactStore.GetPartialPath(execution.VerificationExecutionId, VerificationOutputPurpose.StandardOutput),
                StandardErrorSinkPath = artifactStore.GetPartialPath(execution.VerificationExecutionId, VerificationOutputPurpose.StandardError),
            }, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // ADR-0072: the adapter returned no I/O facts, so no writer was proved closed; any partial file is neither sealed nor
            // deleted here.
            logger.LogError("verification_execution_process_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            await RecordResultAsync(
                execution,
                ticket,
                VerificationExecutionOutcome.Failed,
                null,
                false,
                false,
                false,
                null,
                CancellationToken.None,
                environment.ProfileVersion,
                VerificationExecutionDiagnostic.InvocationUnavailable);
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
            ticket,
            outcome,
            result.ExitCode,
            true,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            result,
            CancellationToken.None,
            environment.ProfileVersion,
            result.IoCompletion.IsComplete
                ? null
                : result.IoCompletion.DeadlineExceeded
                    ? VerificationExecutionDiagnostic.CaptureDeadlineExceeded
                    : VerificationExecutionDiagnostic.CaptureFault);

        // Only a proved exit with complete I/O releases the owned scratch; anything unresolved keeps it (ADR-0072).
        if (result.Outcome == ProcessExecutionOutcome.Exited && result.Termination is not null && result.IoCompletion.IsComplete)
        {
            environmentProvider.ReleaseScratch(execution.VerificationExecutionId);
        }
    }

    private async Task RecordResultAsync(
        EligibleVerificationExecution execution,
        LocalExecutionDispatchTicket? ticket,
        VerificationExecutionOutcome outcome,
        int? exitCode,
        bool captureOutputArtifacts,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        ProcessExecutionResult? processResult,
        CancellationToken cancellationToken,
        string? environmentProfileVersion = null,
        VerificationExecutionDiagnostic? diagnostic = null)
    {
        var evidence = await evidenceReader.CaptureAsync(execution.WorkspacePath, CancellationToken.None);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 is null)
        {
            logger.LogError("verification_execution_completion_evidence_failed ExecutionId={ExecutionId}", execution.VerificationExecutionId);
            return;
        }

        var sealedArtifacts = captureOutputArtifacts
            ? await SealArtifactsAsync(
                execution.VerificationExecutionId, standardOutputTruncated, standardErrorTruncated, processResult?.IoCompletion)
            : [];
        await DispatchAsync(
            new RecordVerificationExecutionResultCommand(
                execution.VerificationExecutionId,
                outcome,
                exitCode,
                evidence.FingerprintSha256,
                sealedArtifacts)
            {
                DispatchTicket = ticket,
                Termination = processResult?.Termination,
                ProcessDuration = processResult?.Duration,
                IoCompletion = processResult?.IoCompletion,
                EnvironmentProfileVersion = environmentProfileVersion,
                Diagnostic = diagnostic,
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<SealedVerificationOutputArtifact>> SealArtifactsAsync(
        Guid verificationExecutionId, bool standardOutputTruncated, bool standardErrorTruncated, ProcessIoCompletion? ioCompletion)
    {
        var results = new List<SealedVerificationOutputArtifact>();
        foreach (var (purpose, truncated, completion) in new[]
                 {
                     (VerificationOutputPurpose.StandardOutput, standardOutputTruncated, ioCompletion?.StandardOutput),
                     (VerificationOutputPurpose.StandardError, standardErrorTruncated, ioCompletion?.StandardError),
                 })
        {
            if (!ProcessIoCompletion.ProvesWriterClosed(completion))
            {
                // The writer was not proved closed: the partial file is neither sealed nor deleted (ADR-0072).
                continue;
            }

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
