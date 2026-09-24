using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordCodeReviewInputAlreadyCodeReviewed;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Codex code-review attempts, entirely outside any EF Core
/// transaction — the entirely isolated Codex + CodeReviewer counterpart to
/// <see cref="AgentAttemptSupervisor"/> (Codex + Planner) and
/// <see cref="ChallengeResolutionSupervisor"/> (Codex + Resolver), sharing the same durable
/// dispatch/recording/recovery pattern and the same provider-agnostic bookkeeping commands
/// (<see cref="RecordAgentAttemptWorkspaceIneligibleCommand"/>,
/// <see cref="RecordAgentAttemptSourceChangedCommand"/>,
/// <see cref="RecordAgentAttemptCheckpointEvidenceUnavailableCommand"/>), plus one
/// code-review-specific dedicated command
/// (<see cref="RecordCodeReviewInputAlreadyCodeReviewedCommand"/>) for the dispatch-gate loss that
/// has no generic equivalent: a competing code-review attempt already completed successfully for
/// the exact same ExecutionReport-plus-verification-evidence input identity while the
/// run/workspace/lease/checkpoint remain fully eligible. This role never mutates the worktree, runs
/// Git, or runs a verification command itself — it only ever invokes the already-bounded, already
/// read-only Codex review adapter. A dispatch marker is committed before the provider is ever
/// invoked so a later polling cycle cannot invoke it twice. Never retries automatically; a host
/// restart leaves any unrecorded attempt for startup reconciliation
/// (<c>ReconcileInterruptedAgentAttemptsCommand</c>, which already covers every non-Implementer
/// Agent role).
/// </summary>
public sealed class ImplementationReviewSupervisor(
    IServiceScopeFactory scopeFactory,
    ICodexImplementationReviewAdapter implementationReviewAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<ImplementationReviewSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Mirrors <c>ChallengeResolutionSupervisor.RecordingTimeout</c>'s own reasoning: a
    /// ReviewChangesRequested outcome independently content-policy-validates and appends up to ten
    /// findings and their events in the same transaction.</summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);

    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var attempts = await DispatchAsync(new GetEligibleCodeReviewAttemptsQuery(), stoppingToken);
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
                logger.LogError("implementation_review_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleCodeReviewAttempt attempt, CancellationToken stoppingToken)
    {
        var preDispatchEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, stoppingToken);
        if (preDispatchEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && preDispatchEvidence.FingerprintSha256 is not null
            && !string.Equals(preDispatchEvidence.FingerprintSha256, attempt.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            await DispatchAsync(
                new RecordAgentAttemptSourceChangedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        if (preDispatchEvidence.Outcome != GitWorkspaceEvidenceOutcome.Success || preDispatchEvidence.FingerprintSha256 is null)
        {
            logger.LogError("implementation_review_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        var launchTarget = await DispatchAsync(new GetCodexLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(
            new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(
                    new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyCodeReviewedCode)
            {
                await DispatchAsync(
                    new RecordCodeReviewInputAlreadyCodeReviewedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        ImplementationReviewInvocationResult invocationResult;
        try
        {
            invocationResult = await implementationReviewAdapter.InvokeAsync(
                new ImplementationReviewInvocationRequest(
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
                    attempt.MaxTotalCapturedBytes),
                stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("implementation_review_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var processSucceeded = invocationResult.Outcome == ImplementationReviewInvocationOutcome.Exited
            && invocationResult.ProcessEvidence is { IsCleanExit: true };
        await RecordResultAsync(
            attempt,
            processSucceeded,
            invocationResult.StandardOutputTruncated,
            invocationResult.StandardErrorTruncated,
            invocationResult.ProviderSessionId,
            invocationResult.ProcessEvidence,
            invocationResult.TokenUsage,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleCodeReviewAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        AgentTokenUsage? tokenUsage,
        CancellationToken cancellationToken)
    {
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionFingerprint = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            ? completionEvidence.FingerprintSha256
            : null;

        if (completionFingerprint is null)
        {
            logger.LogError("implementation_review_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        ValidatedImplementationReview? review = null;
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
            review = await TryParseFinalResponseAsync(sealedArtifacts);
            effectiveOutcome = review is null
                ? AgentOutcome.InvalidStructuredOutput
                : review.IsApproved ? AgentOutcome.ReviewApproved : AgentOutcome.ReviewChangesRequested;
        }

        // A terminal classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled local
        // write must not block shutdown forever). If it still times out or otherwise fails, no
        // terminal result is invented, Codex is never invoked again for this attempt here, and
        // nothing retries in this pass — the attempt simply stays Running and Dispatched, exactly
        // like any other unrecorded attempt, for the next startup's reconciliation.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordImplementationReviewResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordImplementationReviewResultCommand(
                    attempt.RunId, attempt.AttemptId, effectiveOutcome, completionFingerprint, sealedArtifacts, review, providerSessionId,
                    processEvidence,
                    tokenUsage),
                recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            // Covers the bounded timeout elapsing as well as any other dispatch failure. Never
            // the exception object, its message, or any command/environment/output/repository
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("implementation_review_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "implementation_review_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedImplementationReviewArtifact>> SealArtifactsAsync(
        EligibleCodeReviewAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedImplementationReviewArtifact>();
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
                results.Add(new SealedImplementationReviewArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedImplementationReview?> TryParseFinalResponseAsync(IReadOnlyList<SealedImplementationReviewArtifact> sealedArtifacts)
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

        return window.Status != SealedReadStatus.Ok ? null : ImplementationReviewResponseParser.TryParse(window.Text);
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
            logger.LogError("implementation_review_evidence_capture_threw");
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
