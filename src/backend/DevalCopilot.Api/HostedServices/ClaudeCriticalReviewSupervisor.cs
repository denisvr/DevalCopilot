using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewInputAlreadyReviewed;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Claude critical-review attempts, entirely outside any EF Core
/// transaction — the entirely isolated ClaudeCode + CriticalReviewer counterpart to
/// <see cref="AgentAttemptSupervisor"/>, sharing the same durable dispatch/recording/recovery
/// pattern and the same provider-agnostic bookkeeping commands
/// (<see cref="RecordAgentAttemptWorkspaceIneligibleCommand"/>,
/// <see cref="RecordAgentAttemptSourceChangedCommand"/>,
/// <see cref="RecordAgentAttemptCheckpointEvidenceUnavailableCommand"/>), plus one
/// critical-review-specific dedicated command
/// (<see cref="RecordClaudeCriticalReviewInputAlreadyReviewedCommand"/>) for the one dispatch-gate
/// loss that has no Codex-side equivalent: a competing critical review already completed
/// successfully for the exact same input Proposal while the run/workspace/lease/checkpoint remain
/// fully eligible — never conflated with <see cref="AgentOutcome.WorkspaceNoLongerEligible"/>,
/// which stays exclusively about run/workspace/lease/checkpoint loss. This supervisor never shares
/// an eligibility feed, launch-target resolution, adapter, or result-recording command with the
/// Codex-only <see cref="AgentAttemptSupervisor"/>. A dispatch marker is committed before the
/// provider is ever invoked so a later polling cycle cannot invoke it twice. Never retries
/// automatically; a host restart leaves any unrecorded attempt for startup reconciliation.
/// </summary>
public sealed class ClaudeCriticalReviewSupervisor(
    IServiceScopeFactory scopeFactory,
    ICriticalReviewAdapter criticalReviewAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<ClaudeCriticalReviewSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Deliberately independent of <c>stoppingToken</c> in both directions — host
    /// shutdown cannot cancel it early (that would lose a known result) and it cannot block
    /// shutdown indefinitely if the write ever stalls. Longer than
    /// <c>AgentAttemptSupervisor.RecordingTimeout</c> (15s vs. 10s): a Challenged outcome
    /// independently content-policy-validates and appends up to five collaboration messages and
    /// their events in the same transaction, real work neither the Process- nor the single-message
    /// Codex-Proposal-attempt recording path ever performs.</summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Hard ceiling on the bytes read from the sealed final-response artifact before
    /// attempting to parse it. An artifact larger than this is treated as invalid without ever
    /// being read — the bound comes from the read itself, never a separate length pre-check.</summary>
    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var attempts = await DispatchAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), stoppingToken);
                foreach (var attempt in attempts)
                {
                    await ExecuteOneAsync(attempt, stoppingToken);
                }

                // Shared, provider-agnostic bookkeeping feed: workspace/lease/checkpoint
                // ineligibility means the same thing regardless of which supervisor's attempt it
                // is, and RecordAgentAttemptWorkspaceIneligibleCommand is safe to invoke
                // concurrently for the same attempt from both supervisors (the second call simply
                // finds the attempt no longer eligible for this command and no-ops as a failure).
                var ineligible = await DispatchAsync(new GetIneligibleAgentAttemptsQuery(), stoppingToken);
                foreach (var candidate in ineligible)
                {
                    await DispatchAsync(
                        new RecordAgentAttemptWorkspaceIneligibleCommand(candidate.RunId, candidate.AttemptId), CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("claude_critical_review_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleClaudeCriticalReviewAttempt attempt, CancellationToken stoppingToken)
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
            // Never left Running and undispatched on a returned or thrown evidence failure — that
            // would make this attempt eligible again on the very next 500ms poll and silently
            // retry the same failing capture forever, contradicting "never retries automatically."
            // Resolved to a truthful terminal outcome instead; the provider is never invoked.
            logger.LogError("claude_critical_review_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        // Resolved only from the durable, currently successful Claude capability snapshot —
        // never a fresh search through PATH or a private desktop application layout.
        var launchTarget = await DispatchAsync(new GetClaudeLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(
            new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            // The command's own last-gate revalidation is authoritative: workspace/lease/checkpoint
            // eligibility, or the critical-review-specific fact that no competing attempt already
            // reviewed this exact Proposal, could each have been lost between the earlier snapshot
            // and this very call. The two reasons map to two distinct, never-conflated terminal
            // outcomes and recording commands — zero provider invocations either way, and never
            // left to strand. Every other failure reason (not found, already dispatched, no longer
            // Running) means some other path already resolved this attempt, so there is nothing
            // further to do.
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(
                    new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyReviewedCode)
            {
                await DispatchAsync(
                    new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            // Durably committed to dispatch, but there is nothing to invoke: still recorded as a
            // terminal, safe, closed outcome — never left hanging, never silently skipped.
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        CriticalReviewInvocationResult invocationResult;
        try
        {
            invocationResult = await criticalReviewAdapter.InvokeAsync(
                new CriticalReviewInvocationRequest(
                    attempt.RunId,
                    attempt.AttemptId,
                    attempt.WorkspacePath,
                    attempt.ContextManifestRelativeStoragePath,
                    attempt.ContextManifestByteLength,
                    attempt.ContextManifestContentHash,
                    launchTarget.ExecutablePath,
                    attempt.Timeout,
                    attempt.MaxBytesPerStream,
                    attempt.MaxTotalCapturedBytes),
                stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("claude_critical_review_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var processSucceeded = invocationResult.Outcome == CriticalReviewInvocationOutcome.Exited
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
        EligibleClaudeCriticalReviewAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        AgentTokenUsage? tokenUsage,
        CancellationToken cancellationToken)
    {
        // Freshly recaptured after the (read-only) invocation, regardless of its process-level
        // outcome: a changed fingerprint always wins inside CompleteAgent's own override, so an
        // Accepted/Challenged result is never recorded as evidence about stale source. Safely
        // captured: a thrown exception here must never abort recording altogether and leave this
        // already-dispatched attempt stuck Running with no result until a restart — it is treated
        // exactly like a returned capture failure below.
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionFingerprint = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            ? completionEvidence.FingerprintSha256
            : null;

        if (completionFingerprint is null)
        {
            logger.LogError("claude_critical_review_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        ValidatedCriticalReview? review = null;
        AgentOutcome effectiveOutcome;
        if (!processSucceeded)
        {
            effectiveOutcome = AgentOutcome.ProviderInvocationFailed;
        }
        else if (completionFingerprint is null)
        {
            // A null fingerprint means fresh evidence could not be captured at all —
            // CompleteAgent's own mismatch override never fires for a null fingerprint, so
            // without this check a successful provider exit would otherwise still be recorded as
            // a review despite there being no confirmation the checkpoint is still current. Never
            // parse (let alone trust) the final response in that case.
            effectiveOutcome = AgentOutcome.CheckpointEvidenceUnavailable;
        }
        else
        {
            review = await TryParseFinalResponseAsync(attempt, sealedArtifacts);
            effectiveOutcome = review switch
            {
                null => AgentOutcome.InvalidStructuredOutput,
                { IsAcceptance: true } => AgentOutcome.Accepted,
                _ => AgentOutcome.Challenged,
            };
        }

        // A terminal classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled local
        // write must not block shutdown forever). If it still times out or otherwise fails, no
        // terminal result is invented, Claude is never invoked again for this attempt here, and
        // nothing retries in this pass — the attempt simply stays Running and Dispatched, exactly
        // like any other unrecorded attempt, for the next startup's reconciliation.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordClaudeCriticalReviewResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordClaudeCriticalReviewResultCommand(
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
            logger.LogError("claude_critical_review_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "claude_critical_review_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        // Notify only after durable recording — never before.
        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedCriticalReviewArtifact>> SealArtifactsAsync(
        EligibleClaudeCriticalReviewAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedCriticalReviewArtifact>();
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
                results.Add(new SealedCriticalReviewArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedCriticalReview?> TryParseFinalResponseAsync(
        EligibleClaudeCriticalReviewAttempt attempt, IReadOnlyList<SealedCriticalReviewArtifact> sealedArtifacts)
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

        return window.Status != SealedReadStatus.Ok ? null : ClaudeCriticalReviewResponseParser.TryParse(window.Text);
    }

    /// <summary>
    /// Wraps <see cref="IGitWorkspaceEvidenceReader.CaptureAsync"/> so a thrown, non-cancellation
    /// exception is classified exactly like a returned capture failure
    /// (<see cref="GitWorkspaceEvidenceOutcome.GitInvocationFailed"/>) rather than propagating out
    /// and aborting whichever caller was mid-flow — cancellation from actual host shutdown is
    /// still rethrown unchanged.
    /// </summary>
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
            logger.LogError("claude_critical_review_evidence_capture_threw");
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
