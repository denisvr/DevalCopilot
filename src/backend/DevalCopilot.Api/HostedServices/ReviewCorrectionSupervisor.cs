using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionInputAlreadyCorrected;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;

namespace DevalCopilot.Api.HostedServices;

/// <summary>Supervises only durably claimed ReviewCorrection attempts. Dispatch is committed
/// before Claude is invoked; recording is bounded and never causes a second invocation.</summary>
public sealed class ReviewCorrectionSupervisor(
    IServiceScopeFactory scopeFactory,
    IClaudeReviewCorrectionAdapter adapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<ReviewCorrectionSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);
    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var candidates = await DispatchAsync(new GetEligibleReviewCorrectionAttemptsQuery(), stoppingToken);
                foreach (var candidate in candidates)
                {
                    await ExecuteOneAsync(candidate, stoppingToken);
                }

                var ineligible = await DispatchAsync(new GetIneligibleAgentAttemptsQuery(), stoppingToken);
                foreach (var candidate in ineligible)
                {
                    await DispatchAsync(new RecordAgentAttemptWorkspaceIneligibleCommand(candidate.RunId, candidate.AttemptId), CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("review_correction_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleReviewCorrectionAttempt attempt, CancellationToken stoppingToken)
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
            await DispatchAsync(new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        var launchTarget = await DispatchAsync(new GetClaudeLaunchTargetQuery(), stoppingToken);
        var dispatched = await DispatchAsync(new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyCorrectedCode)
            {
                await DispatchAsync(new RecordReviewCorrectionInputAlreadyCorrectedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            await RecordResultAsync(attempt, false, false, false, null, null, stoppingToken);
            return;
        }

        ReviewCorrectionInvocationResult invocation;
        try
        {
            invocation = await adapter.InvokeAsync(new ReviewCorrectionInvocationRequest(
                attempt.RunId, attempt.AttemptId, attempt.WorkspacePath,
                attempt.ContextManifestRelativeStoragePath, attempt.ContextManifestByteLength,
                attempt.ContextManifestContentHash, launchTarget.ExecutablePath, attempt.Timeout,
                attempt.MaxBytesPerStream, attempt.MaxTotalCapturedBytes), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await RecordResultAsync(attempt, false, false, false, null, null, CancellationToken.None);
            return;
        }
        catch (Exception)
        {
            logger.LogError("review_correction_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, false, false, false, null, null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var processSucceeded = invocation.Outcome == ImplementationInvocationOutcome.Exited
            && invocation.ProcessEvidence is { IsCleanExit: true };
        await RecordResultAsync(
            attempt,
            processSucceeded,
            invocation.StandardOutputTruncated,
            invocation.StandardErrorTruncated,
            invocation.ProviderSessionId,
            invocation.ProcessEvidence,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleReviewCorrectionAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        CancellationToken cancellationToken)
    {
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var evidenceAvailable = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && completionEvidence.HeadCommitSha is not null
            && completionEvidence.FingerprintSha256 is not null;

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);
        ValidatedReviewCorrection? correction = null;
        if (processSucceeded)
        {
            var finalResponse = sealedArtifacts.SingleOrDefault(artifact => artifact.Purpose == Domain.Features.Runs.ArtifactPurpose.AgentFinalResponse);
            if (finalResponse is not null && finalResponse.ByteLength <= MaxFinalResponseReadBytes)
            {
                var window = await artifactStore.VerifyAndReadSealedAsync(
                    finalResponse.RelativeStoragePath, finalResponse.ByteLength, finalResponse.ContentHash,
                    0, MaxFinalResponseReadBytes, CancellationToken.None);
                if (window.Status == SealedReadStatus.Ok)
                {
                    correction = ReviewCorrectionResponseParser.TryParse(window.Text, attempt.OrderedInputMessageIds);
                }
            }
        }

        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordReviewCorrectionResultCommandResult> result;
        try
        {
            result = await DispatchAsync(new RecordReviewCorrectionResultCommand(
                attempt.RunId,
                attempt.AttemptId,
                processSucceeded,
                evidenceAvailable ? completionEvidence.HeadCommitSha : null,
                evidenceAvailable ? completionEvidence.FingerprintSha256 : null,
                evidenceAvailable ? completionEvidence.ChangedPaths : [],
                sealedArtifacts,
                correction,
                providerSessionId,
                processEvidence), recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            logger.LogError("review_correction_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (result.IsFailure)
        {
            logger.LogError("review_correction_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}", attempt.AttemptId, result.Errors[0].Code);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRunEventNotifier>()
            .NotifyRunAdvancedAsync(attempt.RunId, result.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedReviewCorrectionArtifact>> SealArtifactsAsync(
        EligibleReviewCorrectionAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedReviewCorrectionArtifact>();
        foreach (var (purpose, truncated) in new[]
                 {
                     (Domain.Features.Runs.ArtifactPurpose.AgentStandardOutput, standardOutputTruncated),
                     (Domain.Features.Runs.ArtifactPurpose.AgentStandardError, standardErrorTruncated),
                     (Domain.Features.Runs.ArtifactPurpose.AgentFinalResponse, false),
                 })
        {
            var sealedFile = await artifactStore.SealAsync(attempt.RunId, attempt.AttemptId, purpose, CancellationToken.None);
            if (sealedFile is not null)
            {
                results.Add(new SealedReviewCorrectionArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<GitWorkspaceEvidenceResult> CaptureEvidenceSafelyAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceReader.CaptureAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("review_correction_evidence_capture_failed");
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
