using System.Data.Common;
using System.Text;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Claims a correction only from a completed, applicable changes-requested review. The
/// exact ordered input messages are persisted before any external invocation can occur.</summary>
public sealed class CreateReviewCorrectionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null,
    ILocalExecutionStepProbe? localStepProbe = null)
    : ICommandHandler<CreateReviewCorrectionAttemptCommand, Result<CreateReviewCorrectionAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;
    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.ReviewCorrection);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateReviewCorrectionAttemptCommandResult>> HandleAsync(
        CreateReviewCorrectionAttemptCommand command, CancellationToken cancellationToken)
    {
        // Supplied direct guidance is normalized (and refused if invalid) before any read or external work. The
        // validator normally rejects it first; this keeps a direct handler call equally safe. Null stays unguided.
        var ticket = command.LocalStep;
        if (ticket is not null
            && (command.Guidance is not null
                || ticket.Kind != LocalExecutionStepKind.ReviewCorrection
                || ticket.SubjectAttemptId != command.ImplementationReviewAttemptId))
        {
            // A local execution sequence step consumes its own exact review and never any caller-supplied guidance.
            return Failure(LocalExecutionSequenceErrors.StepInputMismatch());
        }

        string? directGuidance = null;
        if (command.Guidance is not null)
        {
            directGuidance = DirectHumanGuidance.Normalize(command.Guidance);
            if (directGuidance is null)
            {
                return Failure(DirectHumanGuidanceErrors.Invalid());
            }
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // The execution mode is a durable, immutable admission fact: read afresh (never from the tracked Run)
        // and checked before any workspace, evidence, manifest, or provider work.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<CreateReviewCorrectionAttemptCommandResult>.Failure(executionModeError);
        }

        // A planning sequence (ADR-0055) holds the run against every claim that does not carry its own exact step identity; the
        // database guard of the same predicate decides at the durable commit.
        // A local execution sequence (ADR-0060) holds the run the same way; only its own exact step claims are exempt.
        if (ticket is null
            && (await PlanningSequenceFence.CheckRunAsync(dbContext, run.Id, cancellationToken)
                ?? await LocalExecutionSequenceFence.CheckRunAsync(dbContext, run.Id, cancellationToken)) is { } fenceError)
        {
            return Result<CreateReviewCorrectionAttemptCommandResult>.Failure(fenceError);
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        if (await dbContext.Attempts.AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The run-wide Agent claim budget: every claimed Agent attempt, regardless of role,
        // provider, dispatch, result, or interruption, permanently consumes one slot. Checked
        // first, before any review-correction-specific budget/escalation/authorization logic
        // below, so a globally exhausted run never consumes a review-correction human
        // authorization and never invokes a provider. The filtered unique index on
        // (RunId, AgentBudgetSlot) is the database backstop for the race this count-and-compare
        // check alone cannot close.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent,
            cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        // The independent run-wide Agent invocation-TIME budget (see the companion ADR to
        // ADR-0012), enforced alongside — never instead of — the count-based budget above, still
        // first, before the review-correction-specific budget/escalation/authorization logic
        // below, so a globally time-exhausted run never consumes a review-correction human
        // authorization and never invokes a provider. A run with no time-budget policy (a
        // historical Run predating this decision) skips this check entirely rather than being
        // bound by a fabricated ceiling.
        if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTime)
        {
            var reservedAgentInvocationTime = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: false, cancellationToken);
            if (reservedAgentInvocationTime is null)
            {
                return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Failure(Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and, like them, before the review-correction budget,
        // escalation, and authorization logic below, so a stopped run never consumes a human
        // authorization or creates an escalation. Unconfigured, it reads nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
        if (tokenStopError is not null)
        {
            return Failure(tokenStopError);
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Failure(Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required."));
        }

        if (!await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required."));
        }

        if (ticket is not null
            && await LocalExecutionStepAuthority.CheckEarlyAsync(
                dbContext, ticket, run, LocalExecutionStepKind.ReviewCorrection, workspace, checkpoint, timeProvider.GetUtcNow(), cancellationToken)
                is { } stepError)
        {
            return Failure(stepError);
        }

        // ADR-0057: no other Agent attempt, verification execution or local-commit reservation may own this workspace, decided before
        // any Git work or manifest sealing and again at the write-locked commit seam below.
        if (await WorkspaceExecutionFence.CheckClaimAsync(
            dbContext, run.ProjectId, workspace.Id, cancellationToken, ticket) is { } workspaceBusy)
        {
            return Failure(workspaceBusy);
        }

        // ADR-0058: an additional veto, decided before any Git work or sealing and again at the commit seam below.
        if (await ExecutionCapacityFence.CheckClaimAsync(dbContext, cancellationToken, ticket) is { } capacityRefusal)
        {
            return Failure(capacityRefusal);
        }

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: true, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var review = await dbContext.Attempts.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ImplementationReviewAttemptId && candidate.RunId == run.Id,
            cancellationToken);
        if (review is null
            || review.Kind != AttemptKind.Agent
            || review.AgentRole != AgentRole.CodeReviewer
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.ReviewChangesRequested
            || review.AgentGitWorkspaceId != workspace.Id
            || review.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(review.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review."));
        }

        var persistedFindingCount = await dbContext.CollaborationMessages.CountAsync(
            message => message.AttemptId == review.Id && message.Type == CollaborationMessageType.ReviewFinding,
            cancellationToken);
        if (persistedFindingCount == 0)
        {
            return Failure(Error.Conflict("agent_attempts.review_findings_invalid", "The review has no valid bounded finding set."));
        }

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        var currentReview = ReviewCorrectionReviewEligibility.ResolveCurrent(
            snapshot, run.Id, workspace.Id, checkpoint.Id);
        if (currentReview is null
            || currentReview.ReviewAttempt.Id != review.Id
            || currentReview.ReviewAttempt.AgentCheckpointFingerprintSha256 != checkpoint.FingerprintSha256
            || currentReview.OrderedFindings.Count > ReviewCorrectionOutputSchema.MaximumFindings)
        {
            return Failure(Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review."));
        }

        var executionReport = currentReview.ExecutionReport;
        var findings = currentReview.OrderedFindings;

        if (ticket is not null)
        {
            if (ticket.ReportMessageId != executionReport.Id)
            {
                return Failure(LocalExecutionSequenceErrors.StepInputMismatch());
            }

            // Decided before the ordinary exhaustion branch below, which would record an escalation or consume a Human grant.
            if (await LocalAllowanceReachedAsync(run, ticket, cancellationToken))
            {
                return Failure(LocalExecutionSequenceErrors.AllowanceReached());
            }
        }

        var correctionAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection,
            cancellationToken);
        ReviewCorrectionAuthorization? authorization = null;
        ReviewCorrectionAuthorizationInstruction.HumanGuidance? humanGuidance = null;
        if (correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts)
        {
            // Direct guidance exists only within the ordinary correction budget. At exhaustion the whole request is
            // refused, even when an extra authorization exists: no escalation is created, no authorization is read for
            // consumption, and nothing is sealed or claimed. Existing no-guidance escalation and authorization flows
            // below are unchanged.
            if (directGuidance is not null)
            {
                return Failure(DirectHumanGuidanceErrors.UnavailableAtExhaustion());
            }

            var escalation = await dbContext.ReviewCorrectionEscalations
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
            if (escalation is not null)
            {
                authorization = await dbContext.ReviewCorrectionAuthorizations
                    .Where(candidate => candidate.EscalationId == escalation.Id && candidate.ConsumedByAttemptId == null)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            if (authorization is null)
            {
                return await CreateOrGetEscalationAsync(run, review, executionReport, timeProvider.GetUtcNow(), cancellationToken);
            }

            // Resolved before any manifest is sealed: the authorization, escalation, and HumanInstruction
            // must be one coherent same-run, same-escalation chain, or the claim fails closed without
            // consuming anything. The message is immutable and the authorization row is consumed under
            // its own concurrency token in the single commit below, so this snapshot cannot drift.
            var instruction = await ReviewCorrectionAuthorizationInstruction.ResolveAsync(
                dbContext, run.Id, escalation!, authorization, cancellationToken);
            if (instruction.Error is not null)
            {
                return Failure(instruction.Error);
            }

            humanGuidance = instruction.Guidance;
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null
            || claudeSnapshot.ReasonCode != CapabilityProbeReason.None
            || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Failure(Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var attemptId = ticket?.ReservedOperationId ?? Guid.NewGuid();
        if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext,
                run.Id,
                attemptId,
                checkpoint.Id,
                [executionReport.Id, .. findings.Select(finding => finding.Id)],
                cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction."));
        }

        var manifestJson = ReviewCorrectionContextManifestBuilder.Build(
            run.ProjectId,
            run.Id,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            executionReport.Id,
            executionReport.Summary,
            executionReport.StructuredContentJson,
            findings.Select(finding => new ReviewCorrectionContextManifestBuilder.Finding(
                finding.Id, finding.Summary, finding.StructuredContentJson)).ToArray(),
            evidence.ChangedPaths,
            TrackedChangeEvidence.From(evidence),
            ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext),
            humanGuidance is null ? null : new ReviewCorrectionContextManifestBuilder.Guidance(humanGuidance.MessageId, humanGuidance.Text),
            evidence.UntrackedFiles,
            directGuidance);
        if (Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestArtifactId = Guid.NewGuid();
        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;
        var nowUtc = timeProvider.GetUtcNow();
        // The execution mode is read afresh and guarded by its concurrency token, like the model request: the
        // single SaveChangesAsync below commits only while the stored mode is still the one decided against.
        var guardedModeError = await CurrentRunExecutionMode.ReadAndGuardAgentAsync(dbContext, run, cancellationToken);
        if (guardedModeError is not null)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            return Failure(guardedModeError);
        }

        // Read as late as possible (after every external step) and guarded by the Run's
        // concurrency token, so this Attempt snapshots exactly the model and effort pair that is still current
        // when the single SaveChangesAsync below commits — never an earlier-loaded, stale value.
        var requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);

        // The Claude agentic-turn-limit request gets the same late read and commit-time guard; a stored
        // value outside the accepted range is refused here (with the sealed manifest removed, and before
        // any correction authorization is consumed) rather than clamped or claimed without its limit.
        var requestedTurnLimit = await CurrentClaudeMutationTurnLimit.ReadAndGuardAsync(dbContext, run, cancellationToken);
        if (requestedTurnLimit.IsFailure)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            return Failure(requestedTurnLimit.Errors[0]);
        }

        // The token stop's own commit-time guard: the claim's Run UPDATE also requires the exact stop
        // policy this claim decided against (see CurrentTokenStopPolicy).
        CurrentTokenStopPolicy.Guard(dbContext, run);

        // ADR-0057: the claim's commit seam is one short explicit transaction, opened only now after every external step and every late
        // read. It takes the database write lock, the workspace's ownership is read afresh under it, and only then is anything added or
        // consumed, so ownership cannot change before this claim's own save commits. A refusal disposes it uncommitted and consumes
        // nothing, including a correction authorization.
        IDbContextTransaction fenceTransaction;
        try
        {
            fenceTransaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            throw;
        }
        catch (DbException)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            return Failure(Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded."));
        }

        await using var fenceScope = fenceTransaction;
        // A failed or cancelled fresh read under the write lock leaves nothing persisted by this claim: the uncommitted transaction is
        // disposed, only this operation's own sealed manifest is removed, and a cancellation propagates unchanged.
        Error? workspaceBusyAtSeam;
        LocalExecutionStepLink? stepLink = null;
        try
        {
            if (ticket is not null)
            {
                // A step claim first confirms and locks its exact reserved step, then re-reads everything it consumes under that lock
                // (ADR-0060). A refusal here leaves nothing consumed, in particular no escalation and no Human grant.
                (workspaceBusyAtSeam, stepLink) = await LocalExecutionStepAuthority.ConfirmAtSeamAsync(
                    dbContext, ticket, run, LocalExecutionStepKind.ReviewCorrection, timeProvider.GetUtcNow(), cancellationToken);
                workspaceBusyAtSeam ??= await RevalidateStepInputsAsync(
                    run, workspace.Id, checkpoint.Id, review.Id, executionReport.Id, findings, ticket, cancellationToken);
            }
            else
            {
                workspaceBusyAtSeam = null;
            }

            // ADR-0058: the global ceiling is decided under the same write lock, in the same cleanup, right after the workspace's ownership.
            workspaceBusyAtSeam ??= await WorkspaceExecutionFence.CheckClaimAsync(
                dbContext, run.ProjectId, workspace.Id, cancellationToken, ticket)
                ?? await ExecutionCapacityFence.CheckClaimAsync(dbContext, cancellationToken, ticket);
        }
        catch (OperationCanceledException)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            throw;
        }
        catch (DbException)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            return Failure(ticket is null
                ? Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded.")
                : LocalExecutionSequenceErrors.PersistenceFailed());
        }

        if (workspaceBusyAtSeam is not null)
        {
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            return Failure(workspaceBusyAtSeam);
        }

        var attempt = Attempt.ClaimAgentReviewCorrectionWithModelRequest(
            attemptId,
            run.Id,
            attemptNumber,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            manifestArtifactId,
            InvocationTimeout,
            MaxBytesPerStream,
            MaxTotalCapturedBytes,
            nowUtc,
            requestedClaude.Model,
            requestedClaude.Effort,
            agentBudgetSlot,
            requestedTurnLimit.Value,
            directGuidance);
        authorization?.Consume(attemptId, nowUtc);
        dbContext.Attempts.Add(attempt);

        var inputRows = new List<AttemptInputMessage>(findings.Count + 1)
        {
            AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReport.Id, 0),
        };
        inputRows.AddRange(findings.Select((finding, index) => AttemptInputMessage.Record(
            Guid.NewGuid(), attemptId, finding.Id, index + 1)));
        dbContext.AttemptInputMessages.AddRange(inputRows);

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(manifestArtifact);

        // The step link, its event, the attempt, its inputs and the manifest artifact commit in this one transaction.
        stepLink?.Apply(dbContext, attemptId, InvocationTimeout, nowUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await fenceTransaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (ticket is not null)
        {
            // Cancellation never proves absence: only an independent read of the step decides the sealed file's fate.
            await RollbackBestEffortAsync(fenceTransaction);
            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            throw;
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // The explicit commit seam is released first, so the readback below sees only what actually committed.
            await RollbackBestEffortAsync(fenceTransaction);

            if (ticket is not null)
            {
                return await ConcludeStepFailureAsync(
                    ticket, exception, attempt, inputRows, manifestArtifact, attemptId, attemptNumber, run.Id);
            }

            var thisAttemptPersisted = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, cancellationToken);
            if (thisAttemptPersisted)
            {
                return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                    new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(attemptId, attemptNumber));
            }

            await CleanUpManifestAsync(run.Id, attemptId, ticket);
            dbContext.Attempts.Remove(attempt);
            foreach (var inputMessage in inputRows)
            {
                dbContext.AttemptInputMessages.Remove(inputMessage);
            }

            dbContext.Artifacts.Remove(manifestArtifact);

            if (exception is DbUpdateConcurrencyException)
            {
                // The Run's lifecycle or Claude model request changed between this claim's late
                // read and its commit (see CurrentClaudeModelPreference); the whole batch rolled
                // back, so nothing persisted and a retry re-reads the current state.
                // A token stop policy change is named as such; anything else keeps the generic
                // run-changed conflict. The refused authorization stays unconsumed either way: the
                // whole batch, including its Consume, rolled back.
                return Failure(
                    await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentRunExecutionMode.ChangedDuringClaim()
                        : await CurrentTokenStopPolicy.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentTokenStopPolicy.PolicyChangedDuringClaim()
                        : CurrentClaudeModelPreference.RunChangedDuringClaim());
            }

            if (await LocalExecutionSequenceFence.RefusedRunSaveAsync(dbContext, run.Id))
            {
                // The database guard of a local execution sequence refused this claim at the write seam (ADR-0060).
                return Failure(LocalExecutionSequenceErrors.ManualControlBlocked());
            }

            if (await PlanningSequenceFence.RefusedRunSaveAsync(dbContext, run.Id))

            {

                // The database guard of an armed sequence refused this claim at the write seam (ADR-0055).

                return Result<CreateReviewCorrectionAttemptCommandResult>.Failure(PlanningSequenceErrors.ManualControlBlocked());

            }


            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            // The race the (RunId, AgentBudgetSlot) unique index exists to close: a concurrent
            // request already consumed the exact slot this request also computed. Below the
            // maximum, this is a safe, retryable conflict — only this one slot number was lost to
            // a faster concurrent claim, not the run's whole budget. Only when the run's real
            // Agent-attempt count has already reached its maximum is this truthfully exhaustion.
            var slotAlreadyClaimedByAnotherAttempt = await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                cancellationToken);
            if (slotAlreadyClaimedByAnotherAttempt)
            {
                var agentAttemptsUsedNow = await dbContext.Attempts.AsNoTracking().CountAsync(
                    candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
                if (agentAttemptsUsedNow >= run.MaximumAgentAttempts)
                {
                    return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
                }

                // Distinguishes a genuine time-budget rejection from a merely lost slot race: a
                // concurrent claim that also committed real reserved time can push this run over
                // its time ceiling even while count capacity remains, and that must never be
                // misreported as a retryable slot conflict.
                if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTimeOnRace)
                {
                    var reservedAgentInvocationTimeNow = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: true, cancellationToken);
                    if (reservedAgentInvocationTimeNow is null)
                    {
                        return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                    if (projectedAgentInvocationTimeOnRace is null)
                    {
                        return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                    {
                        return Failure(Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                    }
                }

                // A concurrent claim that also concluded with recorded usage can carry this provider over
                // its token stop even while count capacity remains; that is never a retryable slot conflict.
                var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
                if (tokenStopOnRace is not null)
                {
                    return Failure(tokenStopOnRace);
                }

                return Failure(Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
            }

            return Failure(Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded."));
        }

        return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
            new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>Removes this claim's sealed manifest. A step claim removes it only when an independent read proves that no worker claimed the
    /// step: a duplicate worker that lost the compare-and-set shares the winner's sealed path, and absence or uncertainty never authorizes
    /// deleting a possibly referenced file.</summary>
    private async Task CleanUpManifestAsync(Guid runId, Guid attemptId, LocalExecutionStepTicket? ticket)
    {
        if (ticket is null || await ProbeStepAsync(ticket) == AttemptDurabilityCheckResult.NotPersisted)
        {
            artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        }
    }

    private async Task<AttemptDurabilityCheckResult> ProbeStepAsync(LocalExecutionStepTicket ticket) =>
        localStepProbe is null
            ? AttemptDurabilityCheckResult.Unresolved
            : await localStepProbe.CheckAsync(ticket.StepId, CancellationToken.None) switch
            {
                LocalExecutionStepProbeResult.Claimed => AttemptDurabilityCheckResult.Persisted,
                LocalExecutionStepProbeResult.NotClaimed => AttemptDurabilityCheckResult.NotPersisted,
                _ => AttemptDurabilityCheckResult.Unresolved,
            };

    /// <summary>The outcome of a failed or ambiguous save of a step claim, decided only by the independent step probe. It is never a retry
    /// classification: the sequence decides what happens next.</summary>
    private async Task<Result<CreateReviewCorrectionAttemptCommandResult>> ConcludeStepFailureAsync(
        LocalExecutionStepTicket ticket,
        Exception exception,
        Attempt attempt,
        List<AttemptInputMessage> inputRows,
        Artifact manifestArtifact,
        Guid attemptId,
        int attemptNumber,
        Guid runId)
    {
        var durability = await ProbeStepAsync(ticket);
        if (durability == AttemptDurabilityCheckResult.Persisted)
        {
            return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(attemptId, attemptNumber));
        }

        if (durability == AttemptDurabilityCheckResult.Unresolved)
        {
            return Failure(LocalExecutionSequenceErrors.PersistenceUnresolved());
        }

        artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        dbContext.Attempts.Remove(attempt);
        dbContext.AttemptInputMessages.RemoveRange(inputRows);
        dbContext.Artifacts.Remove(manifestArtifact);
        return Failure(exception is DbUpdateConcurrencyException
            ? LocalExecutionSequenceErrors.StepAuthorityLost()
            : LocalExecutionSequenceErrors.PersistenceFailed());
    }

    /// <summary>The allowance of a step claim: the run's ordinary correction count (any status, both origins) and the sequence's pinned
    /// allowance, counted over its non-revoked correction steps other than this one. Never reads or consumes a Human grant.</summary>
    private async Task<bool> LocalAllowanceReachedAsync(Run run, LocalExecutionStepTicket ticket, CancellationToken cancellationToken)
    {
        var used = await dbContext.Attempts.AsNoTracking().CountAsync(
            candidate => candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection,
            cancellationToken);
        if (used >= run.MaximumReviewCorrectionAttempts)
        {
            return true;
        }

        var allowance = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(sequence => sequence.Id == ticket.SequenceId)
            .Select(sequence => (int?)sequence.CorrectionAllowance)
            .SingleOrDefaultAsync(cancellationToken);
        if (allowance is not { } pinned)
        {
            return true;
        }

        var spent = await dbContext.LocalExecutionSequenceSteps.AsNoTracking().CountAsync(
            step => step.SequenceId == ticket.SequenceId
                && step.Id != ticket.StepId
                && (step.Kind == LocalExecutionStepKind.ReviewCorrection || step.Kind == LocalExecutionStepKind.DiagnosisCorrection)
                && step.State != LocalExecutionSequenceStepState.Revoked,
            cancellationToken);
        return spent >= pinned;
    }

    /// <summary>Re-reads, untracked and under the claim's write lock, every input a step claim consumes: the run's lifecycle and budgets,
    /// the exact applicable review with its report and complete ordered findings, the allowance, the Claude runtime and the absence of a
    /// competing correction. The allowance is decided before anything could escalate or consume a grant.</summary>
    private async Task<Error?> RevalidateStepInputsAsync(
        Run run,
        Guid workspaceId,
        Guid checkpointId,
        Guid reviewId,
        Guid reportId,
        IReadOnlyList<CollaborationMessage> findings,
        LocalExecutionStepTicket ticket,
        CancellationToken cancellationToken)
    {
        var lifecycle = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => (RunLifecycle?)candidate.Lifecycle)
            .SingleOrDefaultAsync(cancellationToken);
        if (lifecycle != RunLifecycle.Running)
        {
            return Error.Conflict("runs.not_running", "The run is not active.");
        }

        if (await LocalAllowanceReachedAsync(run, ticket, cancellationToken))
        {
            return LocalExecutionSequenceErrors.AllowanceReached();
        }

        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.");
        }

        var agentAttemptsUsed = await dbContext.Attempts.AsNoTracking().CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts.");
        }

        if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTime)
        {
            var reserved = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: true, cancellationToken);
            var projected = reserved is null ? null : AgentInvocationTimeReservation.ComputeProjectedReservation(reserved.Value, InvocationTimeout);
            if (projected is null)
            {
                return Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable.");
            }

            if (projected.Value > maximumAgentInvocationTime)
            {
                return Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time.");
            }
        }

        if (await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken) is { } tokenStop)
        {
            return tokenStop;
        }

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        var still = ReviewCorrectionReviewEligibility.ResolveCurrent(snapshot, run.Id, workspaceId, checkpointId);
        if (still is null
            || still.ReviewAttempt.Id != reviewId
            || still.ExecutionReport.Id != reportId
            || ticket.ReportMessageId != reportId
            || still.OrderedFindings.Count == 0
            || still.OrderedFindings.Count > ReviewCorrectionOutputSchema.MaximumFindings
            || !still.OrderedFindings.Select(finding => finding.Id).SequenceEqual(findings.Select(finding => finding.Id)))
        {
            return Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review.");
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null
            || claudeSnapshot.ReasonCode != CapabilityProbeReason.None
            || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available.");
        }

        return await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
            dbContext, run.Id, ticket.ReservedOperationId, checkpointId, [reportId, .. findings.Select(finding => finding.Id)], cancellationToken)
            ? Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction.")
            : null;
    }

    private async Task<Result<CreateReviewCorrectionAttemptCommandResult>> CreateOrGetEscalationAsync(
        Run run,
        Attempt review,
        CollaborationMessage executionReport,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ReviewCorrectionEscalations
            .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
        if (existing is not null)
        {
            var existingEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, existing.CollaborationMessageId, cancellationToken);
            if (existingEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_escalations.event_missing",
                    "The persisted escalation event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                new CreateReviewCorrectionAttemptCommandResult.Escalated(existing.Id, existing.CollaborationMessageId, sequence));
        }

        var message = CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(),
            CollaborationMessageType.Escalation,
            executionReport.Id,
            "Review correction requires an explicit human decision.",
            "{\"unresolvedDecision\":\"The implementation review requested changes and remains unresolved.\",\"options\":\"Authorize one additional correction attempt, or stop and review the evidence manually.\",\"consequences\":\"Continuing authorizes one bounded correction claim; stopping leaves the run awaiting human action.\",\"evidence\":\"The configured review-correction attempt limit was reached.\",\"recommendedChoice\":\"Stop unless the next correction is explicitly justified.\"}",
            CollaborationMessageProvenance.HostConstructed,
            nowUtc);
        var escalation = ReviewCorrectionEscalation.Record(Guid.NewGuid(), run.Id, review.Id, message.Id, nowUtc);
        dbContext.CollaborationMessages.Add(message);
        dbContext.ReviewCorrectionEscalations.Add(escalation);
        var escalationEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, message.Actor,
            "{\"messageId\":\"" + message.Id + "\",\"type\":\"Escalation\",\"provenance\":\"HostConstructed\"}", nowUtc);
        dbContext.Events.Add(escalationEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrent = await dbContext.ReviewCorrectionEscalations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            var concurrentEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, concurrent.CollaborationMessageId, cancellationToken);
            if (concurrentEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_escalations.event_missing",
                    "The persisted escalation event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                new CreateReviewCorrectionAttemptCommandResult.Escalated(concurrent.Id, concurrent.CollaborationMessageId, sequence));
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(run.Id, escalationEvent.Sequence, cancellationToken);
        }

        return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
            new CreateReviewCorrectionAttemptCommandResult.Escalated(escalation.Id, message.Id, escalationEvent.Sequence));
    }

    private async Task<long?> FindCollaborationMessageEventSequenceAsync(
        Guid runId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(messageId.ToString()))
            .Select(candidate => (long?)candidate.Sequence)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Brings the release of an explicit commit seam forward; a disposed, uncommitted transaction rolls back on its own.</summary>
    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ObjectDisposedException)
        {
            // The enclosing scope disposes the transaction again as a safe no-op.
        }
    }

    private static Result<CreateReviewCorrectionAttemptCommandResult> Failure(Error error) =>
        Result<CreateReviewCorrectionAttemptCommandResult>.Failure(error);
}
