using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionInputAlreadyResolved;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Codex challenge-resolution attempts, entirely outside any EF
/// Core transaction — the entirely isolated Codex + Resolver counterpart to both
/// <see cref="AgentAttemptSupervisor"/> (Codex + Planner) and
/// <see cref="ClaudeCriticalReviewSupervisor"/> (ClaudeCode + CriticalReviewer), sharing the same
/// durable dispatch/recording/recovery pattern and the same provider-agnostic bookkeeping
/// commands (<see cref="RecordAgentAttemptWorkspaceIneligibleCommand"/>,
/// <see cref="RecordAgentAttemptSourceChangedCommand"/>,
/// <see cref="RecordAgentAttemptCheckpointEvidenceUnavailableCommand"/>), plus one
/// challenge-resolution-specific dedicated command
/// (<see cref="RecordChallengeResolutionInputAlreadyResolvedCommand"/>) for the dispatch-gate
/// loss that has no generic equivalent: a competing challenge-resolution attempt already
/// completed successfully for the exact same ordered input set (the original Proposal plus its
/// complete Challenge set) while the run/workspace/lease/checkpoint remain fully eligible — never
/// conflated with <see cref="AgentOutcome.WorkspaceNoLongerEligible"/>, which stays exclusively
/// about run/workspace/lease/checkpoint loss. Mirrors
/// <see cref="ClaudeCriticalReviewSupervisor"/>'s own
/// <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommand</c> handling exactly, one level
/// further down the collaboration protocol. This supervisor never shares an eligibility feed,
/// launch-target resolution, adapter, or result-recording command with either sibling
/// supervisor, even though it shares the
/// Codex provider with <see cref="AgentAttemptSupervisor"/>. A dispatch marker is committed before
/// the provider is ever invoked so a later polling cycle cannot invoke it twice. Never retries
/// automatically; a host restart leaves any unrecorded attempt for startup reconciliation.
/// </summary>
public sealed class ChallengeResolutionSupervisor(
    IServiceScopeFactory scopeFactory,
    ICodexChallengeResolutionAdapter challengeResolutionAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<ChallengeResolutionSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Mirrors <c>ClaudeCriticalReviewSupervisor.RecordingTimeout</c>'s own reasoning:
    /// a Resolved outcome independently content-policy-validates and appends up to six
    /// collaboration messages (up to five Decisions plus one revised Proposal) and their events
    /// in the same transaction.</summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Hard ceiling on the bytes read from the sealed final-response artifact before
    /// attempting to parse it.</summary>
    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var attempts = await DispatchAsync(new GetEligibleChallengeResolutionAttemptsQuery(), stoppingToken);
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
                logger.LogError("challenge_resolution_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleChallengeResolutionAttempt attempt, CancellationToken stoppingToken)
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
            logger.LogError("challenge_resolution_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        var launchTarget = await DispatchAsync(new GetCodexLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(
            new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            // The command's own last-gate revalidation is authoritative: workspace/lease/checkpoint
            // eligibility, or the resolution-specific fact that no competing attempt already
            // resolved this exact ordered input set, could each have been lost between the earlier
            // snapshot and this very call. The two reasons map to two distinct, never-conflated
            // terminal outcomes and recording commands — zero provider invocations either way.
            // Every other failure reason means some other path already resolved this attempt, so
            // there is nothing further to do.
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(
                    new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyResolvedCode)
            {
                await DispatchAsync(
                    new RecordChallengeResolutionInputAlreadyResolvedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, CancellationToken.None);
            return;
        }

        ChallengeResolutionInvocationResult invocationResult;
        try
        {
            invocationResult = await challengeResolutionAdapter.InvokeAsync(
                new ChallengeResolutionInvocationRequest(
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
            logger.LogError("challenge_resolution_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var processSucceeded = invocationResult.Outcome == ChallengeResolutionInvocationOutcome.Exited
            && invocationResult.ProcessEvidence is { IsCleanExit: true };
        await RecordResultAsync(
            attempt,
            processSucceeded,
            invocationResult.StandardOutputTruncated,
            invocationResult.StandardErrorTruncated,
            invocationResult.ProviderSessionId,
            invocationResult.ProcessEvidence,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleChallengeResolutionAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        CancellationToken cancellationToken)
    {
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionFingerprint = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            ? completionEvidence.FingerprintSha256
            : null;

        if (completionFingerprint is null)
        {
            logger.LogError("challenge_resolution_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        ValidatedChallengeResolution? resolution = null;
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
            resolution = await TryParseFinalResponseAsync(attempt, sealedArtifacts);
            effectiveOutcome = resolution is null ? AgentOutcome.InvalidStructuredOutput : AgentOutcome.Resolved;
        }

        // A terminal classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled local
        // write must not block shutdown forever). If it still times out or otherwise fails, no
        // terminal result is invented, Codex is never invoked again for this attempt here, and
        // nothing retries in this pass — the attempt simply stays Running and Dispatched, exactly
        // like any other unrecorded attempt, for the next startup's reconciliation.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordChallengeResolutionResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordChallengeResolutionResultCommand(
                    attempt.RunId, attempt.AttemptId, effectiveOutcome, completionFingerprint, sealedArtifacts, resolution, providerSessionId,
                    processEvidence),
                recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            // Covers the bounded timeout elapsing as well as any other dispatch failure. Never
            // the exception object, its message, or any command/environment/output/repository
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("challenge_resolution_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "challenge_resolution_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedChallengeResolutionArtifact>> SealArtifactsAsync(
        EligibleChallengeResolutionAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedChallengeResolutionArtifact>();
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
                results.Add(new SealedChallengeResolutionArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedChallengeResolution?> TryParseFinalResponseAsync(
        EligibleChallengeResolutionAttempt attempt, IReadOnlyList<SealedChallengeResolutionArtifact> sealedArtifacts)
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

        return window.Status != SealedReadStatus.Ok
            ? null
            : ChallengeResolutionResponseParser.TryParse(window.Text, attempt.ChallengeMessageIds.ToHashSet());
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
            logger.LogError("challenge_resolution_evidence_capture_threw");
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
