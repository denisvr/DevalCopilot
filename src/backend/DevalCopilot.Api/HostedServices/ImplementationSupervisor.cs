using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationInputAlreadyImplemented;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Claude implementation attempts, entirely outside any EF Core
/// transaction — the ClaudeCode + Implementer counterpart to
/// <see cref="ChallengeResolutionSupervisor"/>, sharing the same durable
/// dispatch/recording/recovery pattern and the same provider-agnostic pre-dispatch bookkeeping
/// commands, plus one implementation-specific dedicated command
/// (<see cref="RecordImplementationInputAlreadyImplementedCommand"/>) for the dispatch-gate loss
/// that has no generic equivalent.
///
/// Unlike every sibling supervisor, this one always independently re-reads fresh Git evidence
/// after invoking the provider — regardless of whether the process itself succeeded, failed, or
/// threw — because this is the one role whose invocation can genuinely mutate the worktree.
/// <see cref="RecordImplementationResultCommandHandler"/> is the single place that classifies the
/// terminal outcome from that evidence plus the parsed report; this supervisor only gathers the
/// raw ingredients and hands them over, never pre-deciding the outcome itself. A dispatch marker
/// is committed before the provider is ever invoked so a later polling cycle cannot invoke it
/// twice. Never retries automatically, never rolls back a mutation; a host restart leaves any
/// unrecorded attempt for <c>ReconcileInterruptedImplementationAttemptsCommand</c> at startup.
/// </summary>
public sealed class ImplementationSupervisor(
    IServiceScopeFactory scopeFactory,
    IClaudeImplementationAdapter implementationAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<ImplementationSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>An Implemented outcome creates a new checkpoint plus its changed-file rows and
    /// appends one ExecutionReport message and event, all in the same transaction — a bounded
    /// bit more work than a resolution's own recording, but still comfortably short.</summary>
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
                var attempts = await DispatchAsync(new GetEligibleImplementationAttemptsQuery(), stoppingToken);
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
                logger.LogError("implementation_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleImplementationAttempt attempt, CancellationToken stoppingToken)
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
            logger.LogError("implementation_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        var launchTarget = await DispatchAsync(new GetClaudeLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(
            new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(
                    new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }
            else if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.InputAlreadyImplementedCode)
            {
                await DispatchAsync(
                    new RecordImplementationInputAlreadyImplementedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, observedModel: null,
                observedEffort: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        ImplementationInvocationResult invocationResult;
        try
        {
            invocationResult = await implementationAdapter.InvokeAsync(
                new ImplementationInvocationRequest(
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
            // Deliberately still falls through to RecordResultAsync below, which always
            // independently re-reads fresh Git evidence regardless of how invocation ended —
            // this exception path is never treated differently from an ordinary process
            // failure, since the worktree may have been mutated before the exception occurred.
            logger.LogError("implementation_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, processSucceeded: false, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, observedModel: null,
                observedEffort: null, processEvidence: null, tokenUsage: null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var processSucceeded = invocationResult.Outcome == ImplementationInvocationOutcome.Exited
            && invocationResult.ProcessEvidence is { IsCleanExit: true };
        await RecordResultAsync(
            attempt,
            processSucceeded,
            invocationResult.StandardOutputTruncated,
            invocationResult.StandardErrorTruncated,
            invocationResult.ProviderSessionId,
            invocationResult.ObservedModel,
            invocationResult.ObservedEffort,
            invocationResult.ProcessEvidence,
            invocationResult.TokenUsage,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleImplementationAttempt attempt,
        bool processSucceeded,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        string? observedModel,
        string? observedEffort,
        AgentProcessEvidence? processEvidence,
        AgentTokenUsage? tokenUsage,
        CancellationToken cancellationToken)
    {
        // Always captured, regardless of processSucceeded: this is the one role whose
        // invocation can mutate the worktree even when the process itself failed or was
        // cancelled, so RecordImplementationResultCommandHandler must always be given a
        // truthful account of the post-invocation worktree state to classify the outcome from.
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionSucceeded = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && completionEvidence.HeadCommitSha is not null
            && completionEvidence.FingerprintSha256 is not null;
        if (!completionSucceeded)
        {
            logger.LogError("implementation_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        ValidatedImplementationReport? report = null;
        if (processSucceeded)
        {
            report = await TryParseFinalResponseAsync(attempt, sealedArtifacts);
        }

        // A terminal classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled local
        // write must not block shutdown forever). If it still times out or otherwise fails, no
        // terminal result is invented, Claude is never invoked again for this attempt here, and
        // nothing retries in this pass — the attempt simply stays Running and Dispatched, exactly
        // like any other unrecorded attempt, for the next startup's reconciliation (which, for
        // Implementer attempts specifically, independently re-checks fresh Git evidence before
        // deciding whether the workspace must also be flagged NeedsAttention).
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordImplementationResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordImplementationResultCommand(
                    attempt.RunId,
                    attempt.AttemptId,
                    processSucceeded,
                    completionSucceeded ? completionEvidence.HeadCommitSha : null,
                    completionSucceeded ? completionEvidence.FingerprintSha256 : null,
                    completionSucceeded ? completionEvidence.ChangedPaths : [],
                    sealedArtifacts,
                    report,
                    providerSessionId,
                    observedModel,
                    observedEffort,
                    processEvidence,
                    tokenUsage),
                recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            // Covers the bounded timeout elapsing as well as any other dispatch failure. Never
            // the exception object, its message, or any command/environment/output/repository
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("implementation_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "implementation_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedImplementationArtifact>> SealArtifactsAsync(
        EligibleImplementationAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedImplementationArtifact>();
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
                results.Add(new SealedImplementationArtifact(
                    purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedImplementationReport?> TryParseFinalResponseAsync(
        EligibleImplementationAttempt attempt, IReadOnlyList<SealedImplementationArtifact> sealedArtifacts)
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

        return window.Status != SealedReadStatus.Ok ? null : ImplementationResponseParser.TryParse(window.Text);
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
            logger.LogError("implementation_evidence_capture_threw");
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
