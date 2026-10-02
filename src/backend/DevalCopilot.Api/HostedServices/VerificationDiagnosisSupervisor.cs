using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisDispatchRefusal;
using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Codex verification-diagnosis attempts (ADR-0018), entirely outside any EF Core transaction —
/// the read-only counterpart to <see cref="ImplementationReviewSupervisor"/> with its own eligibility feed, adapter, and
/// result-recording command, never shared with the ordinary review. It never mutates the worktree, runs Git, or runs a
/// verification command; it only invokes the already-bounded, read-only Codex adapter. A dispatch marker is committed before
/// the provider is ever invoked so a later polling cycle cannot invoke it twice. It never retries automatically and never
/// repairs the format of an invalid response; a host restart leaves any unrecorded attempt for startup reconciliation.
/// </summary>
public sealed class VerificationDiagnosisSupervisor(
    IServiceScopeFactory scopeFactory,
    ICodexVerificationDiagnosisAdapter diagnosisAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<VerificationDiagnosisSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>A findings outcome independently content-policy-validates and appends up to ten findings and their events in
    /// the same transaction.</summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);

    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var attempts = await DispatchAsync(new GetEligibleVerificationDiagnosisAttemptsQuery(), stoppingToken);
                foreach (var attempt in attempts)
                {
                    await ExecuteOneAsync(attempt, stoppingToken);
                }

                var ineligible = await DispatchAsync(new GetIneligibleAgentAttemptsQuery(), stoppingToken);
                foreach (var candidate in ineligible)
                {
                    await DispatchAsync(
                        new RecordAgentAttemptWorkspaceIneligibleCommand(candidate.RunId, candidate.AttemptId), CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("verification_diagnosis_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleVerificationDiagnosisAttempt attempt, CancellationToken stoppingToken)
    {
        var preDispatchEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, stoppingToken);
        if (preDispatchEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && preDispatchEvidence.FingerprintSha256 is not null
            && !string.Equals(preDispatchEvidence.FingerprintSha256, attempt.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            await DispatchAsync(new RecordAgentAttemptSourceChangedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        if (preDispatchEvidence.Outcome != GitWorkspaceEvidenceOutcome.Success || preDispatchEvidence.FingerprintSha256 is null)
        {
            logger.LogError("verification_diagnosis_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        var launchTarget = await DispatchAsync(new GetCodexLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            var code = dispatched.Errors[0].Code;
            if (code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyDiagnosedCode)
            {
                await DispatchAsync(
                    new RecordVerificationDiagnosisDispatchRefusalCommand(
                        attempt.RunId, attempt.AttemptId, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed),
                    CancellationToken.None);
            }
            else if (code == MarkAgentAttemptDispatchedCommandHandler.VerificationEvidenceChangedCode)
            {
                await DispatchAsync(
                    new RecordVerificationDiagnosisDispatchRefusalCommand(
                        attempt.RunId, attempt.AttemptId, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged),
                    CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            await RecordResultAsync(attempt, processSucceeded: false, false, false, null, null, null);
            return;
        }

        VerificationDiagnosisInvocationResult invocationResult;
        try
        {
            invocationResult = await diagnosisAdapter.InvokeAsync(
                new VerificationDiagnosisInvocationRequest(
                    attempt.RunId,
                    attempt.AttemptId,
                    attempt.WorkspacePath,
                    attempt.ContextManifestRelativeStoragePath,
                    attempt.ContextManifestByteLength,
                    attempt.ContextManifestContentHash,
                    launchTarget.ExecutablePath,
                    launchTarget.ScriptPath,
                    attempt.Timeout,
                    attempt.MaxBytesPerStream,
                    attempt.MaxTotalCapturedBytes,
                    attempt.RequestedModel,
                    attempt.RequestedEffort),
                stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("verification_diagnosis_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, processSucceeded: false, false, false, null, null, null);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently confirms a clean exit.
        var processSucceeded = invocationResult.Outcome == VerificationDiagnosisInvocationOutcome.Exited
            && invocationResult.ProcessEvidence is { IsCleanExit: true };
        await RecordResultAsync(
            attempt,
            processSucceeded,
            invocationResult.StandardOutputTruncated,
            invocationResult.StandardErrorTruncated,
            invocationResult.ProviderSessionId,
            invocationResult.ProcessEvidence,
            invocationResult.TokenUsage);
    }

    private async Task RecordResultAsync(
        EligibleVerificationDiagnosisAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        AgentTokenUsage? tokenUsage)
    {
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionFingerprint = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            ? completionEvidence.FingerprintSha256
            : null;
        if (completionFingerprint is null)
        {
            logger.LogError("verification_diagnosis_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        ValidatedVerificationDiagnosis? diagnosis = null;
        AgentOutcome effectiveOutcome;
        if (!processSucceeded)
        {
            effectiveOutcome = AgentOutcome.ProviderInvocationFailed;
        }
        else if (completionFingerprint is null)
        {
            effectiveOutcome = AgentOutcome.CheckpointEvidenceUnavailable;
        }
        else
        {
            diagnosis = await TryParseFinalResponseAsync(sealedArtifacts);
            effectiveOutcome = diagnosis is null
                ? AgentOutcome.InvalidStructuredOutput
                : diagnosis.IsEscalation ? AgentOutcome.DiagnosisEscalated : AgentOutcome.DiagnosisFindingsRecorded;
        }

        // A terminal classification exists at this point, so this short recording transaction is attempted with its own
        // bounded token: neither the host shutdown token nor None. If it times out or fails, no terminal result is invented,
        // the provider is never invoked again here, and the attempt stays Running and Dispatched for startup reconciliation.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordVerificationDiagnosisResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordVerificationDiagnosisResultCommand(
                    attempt.RunId, attempt.AttemptId, effectiveOutcome, completionFingerprint, sealedArtifacts, diagnosis,
                    providerSessionId, processEvidence, tokenUsage),
                recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            logger.LogError("verification_diagnosis_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "verification_diagnosis_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, CancellationToken.None);
    }

    private async Task<IReadOnlyList<SealedVerificationDiagnosisArtifact>> SealArtifactsAsync(
        EligibleVerificationDiagnosisAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedVerificationDiagnosisArtifact>();
        foreach (var (purpose, truncated) in new[]
                 {
                     (ArtifactPurpose.AgentStandardOutput, standardOutputTruncated),
                     (ArtifactPurpose.AgentStandardError, standardErrorTruncated),
                     (ArtifactPurpose.AgentFinalResponse, false),
                 })
        {
            var sealedFile = await artifactStore.SealAsync(attempt.RunId, attempt.AttemptId, purpose, CancellationToken.None);
            if (sealedFile is not null)
            {
                results.Add(new SealedVerificationDiagnosisArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedVerificationDiagnosis?> TryParseFinalResponseAsync(IReadOnlyList<SealedVerificationDiagnosisArtifact> sealedArtifacts)
    {
        var finalResponse = sealedArtifacts.SingleOrDefault(artifact => artifact.Purpose == ArtifactPurpose.AgentFinalResponse);
        if (finalResponse is null || finalResponse.ByteLength > MaxFinalResponseReadBytes)
        {
            return null;
        }

        var window = await artifactStore.VerifyAndReadSealedAsync(
            finalResponse.RelativeStoragePath,
            finalResponse.ByteLength,
            finalResponse.ContentHash,
            fromOffset: 0,
            maxBytes: MaxFinalResponseReadBytes,
            CancellationToken.None);

        return window.Status != SealedReadStatus.Ok ? null : VerificationDiagnosisResponseParser.TryParse(window.Text);
    }

    private async Task<GitWorkspaceEvidenceResult> CaptureEvidenceSafelyAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceReader.CaptureAsync(workspacePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("verification_diagnosis_evidence_capture_threw");
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
        }
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
