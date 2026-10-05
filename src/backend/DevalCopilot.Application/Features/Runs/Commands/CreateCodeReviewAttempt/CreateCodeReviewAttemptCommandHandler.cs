using System.Data.Common;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;

/// <summary>
/// Claims one durable Codex code-review attempt. Mirrors
/// <c>CreateChallengeResolutionAttemptCommandHandler</c>'s workspace/lease/checkpoint eligibility
/// chain and sealed-manifest/persistence-race handling exactly, plus the additional closed chain of
/// checks that: the named ExecutionReport is real, current, and owned by a successful Claude
/// implementation attempt whose result checkpoint is exactly the workspace's current checkpoint;
/// every currently enabled verification command has a latest terminal execution bound to that exact
/// checkpoint, and every one of them Passed; and this exact ExecutionReport-plus-verification-set
/// input identity is not already successfully reviewed. Every failure fails closed with a stable,
/// path-free reason code — never a partial claim, and never an arbitrarily selected verification
/// execution when several enabled commands exist.
/// </summary>
public sealed class CreateCodeReviewAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe,
    IAccountUsageObserver? accountUsageGuardAdapter = null)
    : ICommandHandler<CreateCodeReviewAttemptCommand, Result<CreateCodeReviewAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.CodeReview);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateCodeReviewAttemptCommandResult>> HandleAsync(
        CreateCodeReviewAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // The execution mode is a durable, immutable admission fact: read afresh (never from the tracked Run)
        // and checked before any workspace, evidence, manifest, or provider work.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(executionModeError);
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start a code-review attempt."));
        }

        // A manual format repair first requires an eligible source (see ReadOnlyFormatRepairSource),
        // evaluated here without workspace context so a request that lost a race to the source's one
        // repair is told the repair was already requested, not that some attempt is running. The
        // context-bound comparison and the exact-input derivation follow once the workspace and
        // checkpoint are known, and both are re-evaluated inside the durable claim transaction. An
        // ordinary request skips all of this and is unaffected.
        if (command.RepairSourceAttemptId is { } earlyRepairSourceAttemptId)
        {
            var earlySource = await ReadOnlyFormatRepairSource.EvaluateAsync(
                dbContext, run.Id, earlyRepairSourceAttemptId, AgentResponseContract.ImplementationReview, null, cancellationToken);
            if (earlySource.Error is { } earlySourceError)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(earlySourceError);
            }
        }

        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The run-wide Agent claim budget: every claimed Agent attempt, regardless of role,
        // provider, dispatch, result, or interruption, permanently consumes one slot. Checked
        // before any provider-availability probe or evidence capture so an exhausted run never
        // invokes a provider. The filtered unique index on (RunId, AgentBudgetSlot) is the
        // database backstop for the race this count-and-compare check alone cannot close.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent,
            cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        // The independent run-wide Agent invocation-TIME budget (see the companion ADR to
        // ADR-0012), enforced alongside — never instead of — the count-based budget above, at the
        // same point: before any provider-availability probe or evidence capture. A run with no
        // time-budget policy (a historical Run predating this decision) skips this check entirely
        // rather than being bound by a fabricated ceiling.
        if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTime)
        {
            var reservedAgentInvocationTime = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: false, cancellationToken);
            if (reservedAgentInvocationTime is null)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and before any provider-availability probe, Git work, or
        // manifest sealing. Unconfigured, it reads nothing and changes nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
        if (tokenStopError is not null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(tokenStopError);
        }

        // The run-scoped Codex account-usage stop (ADR-0025), after the budgets and the token stop and before any provider probe, Git
        // work, or manifest sealing. The strict observation happens here, outside any EF transaction; a refusal commits nothing.
        // Unconfigured, it makes no observation. The commit seam below re-confirms the setting, the launch tuple and freshness.
        var accountUsageCheck = await CodexAccountUsageStopGate.CheckClaimAsync(
            dbContext, accountUsageGuardAdapter, timeProvider, run, cancellationToken);
        if (accountUsageCheck.IsFailure)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(accountUsageCheck.Errors);
        }

        var accountUsageGuard = accountUsageCheck.Value;

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a code review."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a code review."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a code review."));
        }

        // The repair's target: the ExecutionReport and verification set recorded as the source's
        // inputs, validated exactly as an ordinary request's would be and required to equal the
        // currently enabled/latest Passed selection — never supplied by the caller. Decided before any
        // provider probe or Git capture.
        var executionReportMessageId = command.ExecutionReportMessageId ?? Guid.Empty;
        if (command.RepairSourceAttemptId is { } repairSourceAttemptId)
        {
            var repairTarget = await ResolveRepairTargetAsync(
                run.Id, run.ProjectId, repairSourceAttemptId, workspace.Id, checkpoint, cancellationToken);
            if (repairTarget.Error is { } repairTargetError)
            {
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(repairTargetError);
            }

            executionReportMessageId = repairTarget.ExecutionReportMessageId;
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null || codexSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: true, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected result checkpoint is no longer current for this workspace."));
        }

        var executionReportValidation = await ValidateExecutionReportAsync(run.Id, executionReportMessageId, workspace.Id, checkpoint, cancellationToken);
        if (executionReportValidation.Error is { } error)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(error);
        }

        var validatedExecutionReport = executionReportValidation.Value!;
        var executionReportMessage = validatedExecutionReport.ExecutionReport;
        var resolvedPlanMessage = validatedExecutionReport.ImplementedPlan;

        var verificationValidation = await ValidateVerificationEvidenceAsync(run.ProjectId, checkpoint, cancellationToken);
        if (verificationValidation.Error is { } verificationError)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(verificationError);
        }

        var orderedVerificationExecutions = verificationValidation.OrderedExecutions!;

        var alreadyReviewed = await CodeReviewInputIdentity.HasCompetingSuccessfulReviewAsync(
            dbContext,
            run.Id,
            attemptId: Guid.Empty,
            executionReportMessage.Id,
            orderedVerificationExecutions.Select(item => item.Execution.Id).ToArray(),
            cancellationToken);
        if (alreadyReviewed)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.already_code_reviewed", "This exact implementation and verification evidence set already has a successful code review."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var orderedVerificationEvidence = orderedVerificationExecutions
            .Select(item => new CodeReviewContextManifestBuilder.VerificationEvidence(
                item.CommandName, item.CommandNumber, item.Execution.Status.ToString(), item.Execution.Outcome?.ToString(), item.Execution.ExitCode))
            .ToArray();
        var instructions = ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext);
        var manifestJson = validatedExecutionReport.PreviousExecutionReport is null
            ? CodeReviewContextManifestBuilder.Build(
                run.ProjectId,
                workspace.Id,
                checkpoint.Id,
                checkpoint.FingerprintSha256,
                run.Objective,
                resolvedPlanMessage.Id,
                resolvedPlanMessage.Summary,
                resolvedPlanMessage.StructuredContentJson,
                executionReportMessage.Id,
                executionReportMessage.Summary,
                executionReportMessage.StructuredContentJson,
                orderedVerificationEvidence,
                evidence.ChangedPaths,
                TrackedChangeEvidence.From(evidence),
                instructions,
                evidence.UntrackedFiles,
                formatRepair: command.RepairSourceAttemptId is not null)
            : CodeReviewContextManifestBuilder.BuildForCorrection(
                run.ProjectId,
                workspace.Id,
                checkpoint.Id,
                checkpoint.FingerprintSha256,
                run.Objective,
                resolvedPlanMessage.Id,
                resolvedPlanMessage.Summary,
                resolvedPlanMessage.StructuredContentJson,
                executionReportMessage.Id,
                executionReportMessage.Summary,
                executionReportMessage.StructuredContentJson,
                orderedVerificationEvidence,
                evidence.ChangedPaths,
                TrackedChangeEvidence.From(evidence),
                new CodeReviewContextManifestBuilder.CorrectionEvidence(
                    validatedExecutionReport.PreviousExecutionReport.Id,
                    validatedExecutionReport.PreviousExecutionReport.Summary,
                    validatedExecutionReport.PreviousExecutionReport.StructuredContentJson,
                    validatedExecutionReport.OrderedFindings
                        .Select(finding => new CodeReviewContextManifestBuilder.CorrectionFinding(
                            finding.Id, finding.Summary, finding.StructuredContentJson))
                        .ToArray(),
                    validatedExecutionReport.OrderedRevisionResponses
                        .Select(response => new CodeReviewContextManifestBuilder.CorrectionRevisionResponse(
                            response.Id,
                            response.InReplyToMessageId!.Value,
                            response.Summary,
                            response.StructuredContentJson))
                        .ToArray()),
                instructions,
                evidence.UntrackedFiles,
                formatRepair: command.RepairSourceAttemptId is not null);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        // The final durable claim boundary: a genuinely fresh, untracked read of the Run's own
        // current preference, taken only now — after the external Git evidence capture and
        // artifact-sealing work above have both already completed. See CurrentCodexAssignmentPreference.
        var (requestedModel, requestedEffort) = await CurrentCodexAssignmentPreference.ReadAsync(dbContext, run.Id, cancellationToken);

        var attempt = Attempt.ClaimAgentCodeReviewWithAssignment(
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
            requestedModel,
            requestedEffort,
            agentBudgetSlot,
            command.RepairSourceAttemptId);
        CodexAccountUsageStopGate.Snapshot(attempt, accountUsageGuard);

        // The one authoritative record of this attempt's exact durable identity: the reviewed
        // ExecutionReport at sequence 0 (an AttemptInputMessage, exactly like every other Agent
        // role's input), and the exact ordered claimed verification-execution set (a dedicated
        // AttemptVerificationEvidence membership row per execution — never a JSON column).
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReportMessage.Id, sequence: 0);

        var verificationEvidenceRows = new List<AttemptVerificationEvidence>(orderedVerificationExecutions.Count);
        for (var index = 0; index < orderedVerificationExecutions.Count; index++)
        {
            var item = orderedVerificationExecutions[index];
            verificationEvidenceRows.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), attemptId, item.Execution.VerificationCommandId, item.Execution.Id, sequence: index));
        }

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            truncated: false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);

        // The final durable claim boundary: one short, explicit transaction — opened only now,
        // after all external work above has completed — makes the guard check and the Attempt's
        // own commit a single atomic database operation, so no other transaction can commit a
        // conflicting preference change in between. Its connection's own SQLite lock-wait is
        // bounded by Microsoft.Data.Sqlite's own default (30 seconds), confirmed by direct
        // measurement to be a genuine bound rather than an indefinite wait, so a genuine conflict
        // fails within that bound instead of waiting forever. Every step of this boundary —
        // acquiring the transaction, the guard, the save, and the commit — is covered below, and
        // every failure path cleans up the already-sealed manifest file unless a fresh, untracked
        // read confirms the Attempt is durably persisted despite the failure.
        IDbContextTransaction claimTransaction;
        try
        {
            claimTransaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Nothing was ever begun — no tracked entity has been added and no statement could
            // have executed, so the sealed manifest is unconditionally orphaned. Cancellation is
            // never converted into a Result; it propagates exactly as cancellation.
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            throw;
        }
        catch (DbException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            bool preferenceStillCurrent;
            bool stopPolicyStillCurrent;
            Error? accountUsageError;
            bool modeStillAdmitted;
            Error? repairRevalidationError = null;
            try
            {
                // One atomic UPDATE ... WHERE statement requiring the Run's requested model/effort
                // to still exactly match what was just read above.
                preferenceStillCurrent = await CurrentCodexAssignmentPreference.ConfirmUnchangedAsync(
                    dbContext, run.Id, requestedModel, requestedEffort, cancellationToken);

                // The token stop policy the claim decided against must be unchanged too: one more
                // atomic UPDATE ... WHERE compare inside this same transaction (see CurrentTokenStopPolicy).
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);

                // The account-usage stop (ADR-0025): the stored setting and the vetted launch tuple must still be exactly what the
                // early check observed, and its evidence still fresh at this seam, before any insert.
                accountUsageError = await CodexAccountUsageStopGate.ConfirmInTransactionAsync(
                    dbContext, timeProvider, run, accountUsageGuard, cancellationToken);

                // The execution mode the claim decided against must still admit Agent work: one more atomic
                // UPDATE ... WHERE inside this same transaction, so a mode change can never confer stale authority.
                modeStillAdmitted = await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, cancellationToken);

                // A repair re-reads its source, both exact input identities, the current verification
                // selection, and the running slot here, after the two guard writes above hold the
                // database write lock, so they are atomic with the Attempt insert below: a source or
                // input change committed after the earlier checks is seen here, never merely late-read.
                if (preferenceStillCurrent && stopPolicyStillCurrent && accountUsageError is null && command.RepairSourceAttemptId is { } repairSourceAtCommit)
                {
                    repairRevalidationError = await RevalidateRepairAsync(
                        run, repairSourceAtCommit, workspace.Id, checkpoint, executionReportMessage.Id, orderedVerificationExecutions, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // No tracked entity has been added yet at this point either — same unconditional
                // cleanup as acquisition cancellation above. Cancellation is never misclassified
                // as a preference conflict or an ordinary persistence failure; it propagates.
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                throw;
            }
            catch (DbException)
            {
                // A raw provider failure on this guard statement (a bounded lock-wait timeout, or
                // any other unrelated database failure) — never inferred to mean the preference
                // itself changed; that is reported only when the guard actually observes zero
                // matching rows, below.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (!modeStillAdmitted)
            {
                // The run no longer admits Agent work. Rolled back before any insert: no attempt, artifact,
                // reservation, or authorization is consumed, and the sealed manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(CurrentRunExecutionMode.NotAdmitted());
            }

            if (!preferenceStillCurrent)
            {
                // A concurrent preference-only change committed in the gap between the read above
                // and this guard — this claim fails safely, cleaning up the already-sealed
                // manifest file, rather than durably embed a pair that is no longer current.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(Error.Conflict(
                    "agent_attempts.assignment_preference_changed",
                    "The run's requested Codex model or effort changed while this claim was being prepared; retry the request."));
            }

            if (!stopPolicyStillCurrent)
            {
                // A concurrent stop-threshold change committed after this claim decided against
                // the old policy. The transaction is rolled back before any insert, the sealed
                // manifest is removed, and nothing was consumed; a retry re-decides.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            if (accountUsageError is not null)
            {
                // The account-usage stop changed, its launch tuple changed, or its evidence is no longer fresh: rolled back before any
                // insert, the sealed manifest removed and nothing consumed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(accountUsageError);
            }

            if (repairRevalidationError is not null)
            {
                // The source, its inputs, the report chain, or the verification selection changed after
                // the earlier checks: no repair is claimed, nothing is consumed, and the sealed manifest
                // is removed, exactly like a changed preference.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(repairRevalidationError);
            }

            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(inputMessage);
            dbContext.AttemptVerificationEvidence.AddRange(verificationEvidenceRows);
            dbContext.Artifacts.Add(manifestArtifact);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The database may have already applied this insert before the cancellation was
                // observed — the same ambiguity a DbUpdateException here would carry. A fresh,
                // untracked read (using an unconditional token: the caller's own is already
                // cancelled) is the sole authority for the sealed manifest's fate; the caller's
                // cancellation still propagates regardless of what it finds — never silently
                // converted into a Result.
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    dbContext.Attempts.Remove(attempt);
                    dbContext.AttemptInputMessages.Remove(inputMessage);
                    foreach (var row in verificationEvidenceRows)
                    {
                        dbContext.AttemptVerificationEvidence.Remove(row);
                    }

                    dbContext.Artifacts.Remove(manifestArtifact);
                }
                // Persisted or Unresolved: the sealed file is preserved. Unresolved means the
                // independent probe could not establish durable state within its own bound, so
                // this never assumes either outcome. Cancellation still propagates regardless.

                throw;
            }
            catch (DbUpdateException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateCodeReviewAttemptCommandResult>.Success(
                        new CreateCodeReviewAttemptCommandResult(attemptId, attemptNumber, command.RepairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // The independent probe could not establish durable state within its own
                    // bound. The sealed file is preserved exactly as a genuinely persisted
                    // Attempt's would be — it may still be referenced — and this is reported as
                    // unresolved rather than asserted as either success or a definite failure.
                    return Result<CreateCodeReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                dbContext.AttemptInputMessages.Remove(inputMessage);
                foreach (var row in verificationEvidenceRows)
                {
                    dbContext.AttemptVerificationEvidence.Remove(row);
                }

                dbContext.Artifacts.Remove(manifestArtifact);

                if (command.RepairSourceAttemptId is { } repairSourceOnRace
                    && await dbContext.Attempts.AsNoTracking().AnyAsync(
                        candidate => candidate.AgentRepairSourceAttemptId == repairSourceOnRace, cancellationToken))
                {
                    // The race the (AgentRepairSourceAttemptId) unique index exists to close: a
                    // concurrent request already committed the one repair of this source.
                    return Result<CreateCodeReviewAttemptCommandResult>.Failure(Error.Conflict(
                        PlanningRepairSource.AlreadyRequestedCode, "A repair was already requested for this attempt."));
                }

                var competingRunningAttemptExists = await dbContext.Attempts
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
                if (competingRunningAttemptExists)
                {
                    return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                        Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
                }

                // The race the (RunId, AgentBudgetSlot) unique index exists to close: a concurrent
                // request already consumed the exact slot this request also computed. Below the
                // maximum, this is a safe, retryable conflict — only this one slot number was lost
                // to a faster concurrent claim, not the run's whole budget. Only when the run's
                // real Agent-attempt count has already reached its maximum is this truthfully
                // exhaustion.
                var slotAlreadyClaimedByAnotherAttempt = await dbContext.Attempts.AsNoTracking().AnyAsync(
                    candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                    cancellationToken);
                if (slotAlreadyClaimedByAnotherAttempt)
                {
                    var agentAttemptsUsedNow = await dbContext.Attempts.AsNoTracking().CountAsync(
                        candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
                    if (agentAttemptsUsedNow >= run.MaximumAgentAttempts)
                    {
                        return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                            Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
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
                            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                        if (projectedAgentInvocationTimeOnRace is null)
                        {
                            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                        {
                            return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                                Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                        }
                    }

                    // A concurrent claim that also concluded with recorded usage can carry this provider over
                    // its token stop even while count capacity remains; that is never a retryable slot conflict.
                    var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
                    if (tokenStopOnRace is not null)
                    {
                        return Result<CreateCodeReviewAttemptCommandResult>.Failure(tokenStopOnRace);
                    }

                    return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
                }

                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            try
            {
                await claimTransaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The same ambiguity as a commit-time DbException below, resolved the same way —
                // a fresh, untracked read is the sole authority — but cancellation still
                // propagates regardless of what it finds, never converted into a Result.
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    dbContext.Attempts.Remove(attempt);
                    dbContext.AttemptInputMessages.Remove(inputMessage);
                    foreach (var row in verificationEvidenceRows)
                    {
                        dbContext.AttemptVerificationEvidence.Remove(row);
                    }

                    dbContext.Artifacts.Remove(manifestArtifact);
                }
                // Persisted or Unresolved: the sealed file is preserved. Cancellation still
                // propagates regardless.

                throw;
            }
            catch (DbException)
            {
                // The commit's own outcome is uncertain from this exception alone — the database
                // may have actually completed it before the failure became visible to this
                // connection. Never inferred either way: an independent probe is the sole
                // authority for whether the sealed manifest file is an orphan or a durably
                // referenced artifact.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateCodeReviewAttemptCommandResult>.Success(
                        new CreateCodeReviewAttemptCommandResult(attemptId, attemptNumber, command.RepairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // Preserve the sealed file exactly as a genuinely persisted Attempt's would
                    // be; the independent probe could not establish durable state within its own
                    // bound, so this is never asserted as success or failure.
                    return Result<CreateCodeReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                dbContext.AttemptInputMessages.Remove(inputMessage);
                foreach (var row in verificationEvidenceRows)
                {
                    dbContext.AttemptVerificationEvidence.Remove(row);
                }

                dbContext.Artifacts.Remove(manifestArtifact);
                return Result<CreateCodeReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }
        }

        return Result<CreateCodeReviewAttemptCommandResult>.Success(
            new CreateCodeReviewAttemptCommandResult(attempt.Id, attempt.AttemptNumber, command.RepairSourceAttemptId));
    }

    /// <summary>
    /// Rolls back the claim's own guard transaction and releases it, swallowing a failure from
    /// either step: whatever caused the caller's own primary failure already leaves this
    /// transaction's fate uncertain, and a secondary exception here would only mask that primary
    /// failure without changing how this request concludes. The subsequent
    /// <see cref="IAttemptDurabilityProbe"/> check each caller performs is the actual authority on
    /// durable state — through an independent connection, not this transaction's own — so this
    /// method's only job is to give up this transaction's connection and locks as promptly as it
    /// can, best-effort, before that check runs.
    /// </summary>
    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately broad: a rollback attempted after an ambiguous or already-completed
            // commit can throw a provider exception or an InvalidOperationException depending on
            // the transaction's own actual fate, which this method never tries to distinguish.
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception)
            {
                // Best-effort release, same reasoning as above — the enclosing `await using` will
                // also dispose this transaction again once its block exits, which is a safe no-op
                // on an already-disposed transaction.
            }
        }
    }

    /// <summary>
    /// Validates every fact this slice requires about the named ExecutionReport before this attempt
    /// is ever claimed: it belongs to this run, is a provider-observed Claude ExecutionReport, its
    /// owning Implementer attempt actually completed as Implemented, that attempt's real,
    /// verified result checkpoint is exactly the workspace's current checkpoint this review attempt
    /// is about to claim (never merely "a" current checkpoint by coincidence), and it is the one
    /// and only provider-observed ExecutionReport that Implementer attempt ever produced. Both
    /// initial implementation reports and successfully applied correction reports are accepted by
    /// the shared role-first eligibility policy.
    /// </summary>
    private async Task<ExecutionReportValidation> ValidateExecutionReportAsync(
        Guid runId, Guid executionReportMessageId, Guid workspaceId, GitCheckpoint resultCheckpoint, CancellationToken cancellationToken,
        bool asNoTracking = false)
    {
        // A repair's authority reads must reflect the database now, never an entity instance tracked earlier.
        var messages = asNoTracking ? dbContext.CollaborationMessages.AsNoTracking() : dbContext.CollaborationMessages;
        var message = await messages
            .SingleOrDefaultAsync(candidate => candidate.Id == executionReportMessageId, cancellationToken);
        if (message is null || message.RunId != runId)
        {
            return ExecutionReportValidation.Failed(
                Error.NotFound("agent_attempts.execution_report_not_found", "The requested execution report was not found for this run."));
        }

        if (message.Type != CollaborationMessageType.ExecutionReport)
        {
            return ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.not_provider_observed_execution_report",
                    "Only a provider-observed Implementer execution report can be requested for code review."));
        }

        var owningAttempt = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, message, runId, AgentRole.Implementer, cancellationToken, asNoTracking);
        if (owningAttempt is not null
            && (owningAttempt.AgentGitWorkspaceId != workspaceId
                || owningAttempt.AgentResultGitCheckpointId != resultCheckpoint.Id))
        {
            return ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.result_checkpoint_mismatch",
                    "The execution report's result checkpoint is not the workspace's exact current checkpoint."));
        }

        var validation = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, message, runId, workspaceId, resultCheckpoint.Id, cancellationToken);
        return validation is null
            ? ExecutionReportValidation.Failed(
                Error.Conflict(
                    "agent_attempts.implementer_attempt_not_valid",
                    "The execution report's Implementer result chain is not valid for review."))
            : ExecutionReportValidation.Succeeded(validation);
    }

    /// <summary>
    /// Validates the closed verification-evidence chain: at least one verification command is
    /// currently enabled, and every currently enabled command has a latest execution bound to the
    /// exact result checkpoint, terminal, and Passed. Never selects one arbitrary execution when
    /// several enabled commands exist — every enabled command is checked, in a fixed,
    /// deterministic (command-number) order.
    /// </summary>
    private async Task<VerificationEvidenceValidation> ValidateVerificationEvidenceAsync(
        Guid projectId, GitCheckpoint checkpoint, CancellationToken cancellationToken, bool asNoTracking = false)
    {
        var commandSet = asNoTracking ? dbContext.VerificationCommands.AsNoTracking() : dbContext.VerificationCommands;
        var executionSet = asNoTracking ? dbContext.VerificationExecutions.AsNoTracking() : dbContext.VerificationExecutions;
        var enabledCommands = await commandSet
            .Where(command => command.ProjectId == projectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ToListAsync(cancellationToken);
        if (enabledCommands.Count == 0)
        {
            return VerificationEvidenceValidation.Failed(
                Error.Conflict("agent_attempts.no_verification_commands_enabled", "At least one enabled verification command is required to request a code review."));
        }

        var commandIds = enabledCommands.Select(command => command.Id).ToArray();
        var boundExecutions = await executionSet
            .Where(execution =>
                commandIds.Contains(execution.VerificationCommandId)
                && execution.GitCheckpointId == checkpoint.Id
                && execution.CheckpointFingerprintSha256 == checkpoint.FingerprintSha256)
            .ToListAsync(cancellationToken);

        var latestExecutionByCommand = boundExecutions
            .GroupBy(execution => execution.VerificationCommandId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(execution => execution.ExecutionNumber).First());

        var orderedExecutions = new List<(VerificationExecution Execution, string CommandName, int CommandNumber)>(enabledCommands.Count);
        foreach (var command in enabledCommands)
        {
            if (!latestExecutionByCommand.TryGetValue(command.Id, out var latestExecution))
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_missing",
                        $"Enabled verification command '{command.Name}' has no execution bound to the current checkpoint."));
            }

            if (latestExecution.Status == VerificationExecutionStatus.Running)
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_running",
                        $"Enabled verification command '{command.Name}' has no terminal execution bound to the current checkpoint yet."));
            }

            if (latestExecution.Status != VerificationExecutionStatus.Passed)
            {
                return VerificationEvidenceValidation.Failed(
                    Error.Conflict(
                        "agent_attempts.verification_evidence_not_passed",
                        $"Enabled verification command '{command.Name}' has not Passed for the current checkpoint."));
            }

            orderedExecutions.Add((latestExecution, command.Name, command.CommandNumber));
        }

        return VerificationEvidenceValidation.Succeeded(orderedExecutions);
    }

    /// <summary>
    /// Evaluates the repair source (see <see cref="ReadOnlyFormatRepairSource"/>), reads its recorded
    /// ExecutionReport and ordered verification (command, execution) pairs, validates the report chain and
    /// the verification selection exactly as an ordinary request would, and requires the currently
    /// enabled/latest Passed selection to equal the source's recorded set — a rerun, an enabled-command
    /// change, a reorder, or a replacement is an ordinary new review, never this repair. Nothing is supplied
    /// by the caller; a missing referenced record or an unreadable row reads as an ineligible source, never
    /// as the source's 404, and no error echoes a stored value.
    /// </summary>
    private async Task<(Guid ExecutionReportMessageId, Error? Error)> ResolveRepairTargetAsync(
        Guid runId,
        Guid projectId,
        Guid repairSourceAttemptId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var evaluation = await ReadOnlyFormatRepairSource.EvaluateAsync(
            dbContext,
            runId,
            repairSourceAttemptId,
            AgentResponseContract.ImplementationReview,
            new ReadOnlyFormatRepairSource.Context(workspaceId, checkpoint.Id, checkpoint.FingerprintSha256),
            cancellationToken);
        if (evaluation.Error is { } sourceError)
        {
            return (Guid.Empty, sourceError);
        }

        var sourceInputs = await ReadOnlyFormatRepairInputs.ReadAsync(
            dbContext, repairSourceAttemptId, AgentResponseContract.ImplementationReview, cancellationToken);
        if (sourceInputs is null)
        {
            return (Guid.Empty, ReadOnlyFormatRepairSource.Ineligible());
        }

        var reportMessageId = sourceInputs.OrderedMessageIds[0];
        try
        {
            var reportValidation = await ValidateExecutionReportAsync(
                runId, reportMessageId, workspaceId, checkpoint, cancellationToken, asNoTracking: true);
            if (reportValidation.Error is { } reportError)
            {
                return (Guid.Empty, reportError.Code == "agent_attempts.execution_report_not_found"
                    ? ReadOnlyFormatRepairSource.Ineligible()
                    : reportError);
            }

            var verificationValidation = await ValidateVerificationEvidenceAsync(
                projectId, checkpoint, cancellationToken, asNoTracking: true);
            if (verificationValidation.Error is { } verificationError)
            {
                return (Guid.Empty, verificationError);
            }

            var currentVerification = verificationValidation.OrderedExecutions!
                .Select(item => (item.Execution.VerificationCommandId, item.Execution.Id))
                .ToList();
            return currentVerification.SequenceEqual(sourceInputs.OrderedVerification)
                ? (reportMessageId, null)
                : (Guid.Empty, ReadOnlyFormatRepairSource.InputsMismatch());
        }
        catch (InvalidOperationException)
        {
            // A persisted row of the report chain could not be materialized (typically an unparseable
            // stored enum string); fixed refusal that echoes neither the exception nor the stored value.
            return (Guid.Empty, ReadOnlyFormatRepairSource.Ineligible());
        }
    }

    /// <summary>The repair's final in-transaction read, decided against the state the write lock now
    /// protects: the source is still eligible with the same exact inputs, the report and verification
    /// selection the manifest was sealed from are still the current ones, no other attempt already reviewed
    /// this exact identity, and no attempt is Running. Returns the refusal, or null when still eligible.</summary>
    private async Task<Error?> RevalidateRepairAsync(
        Run run,
        Guid repairSourceAttemptId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid sealedExecutionReportMessageId,
        IReadOnlyList<(VerificationExecution Execution, string CommandName, int CommandNumber)> sealedVerification,
        CancellationToken cancellationToken)
    {
        if (await ReadOnlyFormatRepairSource.EvaluateContextStillCurrentAsync(
                dbContext, run.Id, workspaceId, checkpoint.Id, runMayBeCreated: false, cancellationToken) is { } contextError)
        {
            return contextError;
        }

        var target = await ResolveRepairTargetAsync(run.Id, run.ProjectId, repairSourceAttemptId, workspaceId, checkpoint, cancellationToken);
        if (target.Error is { } targetError)
        {
            return targetError;
        }

        if (target.ExecutionReportMessageId != sealedExecutionReportMessageId)
        {
            return ReadOnlyFormatRepairSource.InputsMismatch();
        }

        var sourceInputs = await ReadOnlyFormatRepairInputs.ReadAsync(
            dbContext, repairSourceAttemptId, AgentResponseContract.ImplementationReview, cancellationToken);
        if (sourceInputs is null
            || !sealedVerification
                .Select(item => (item.Execution.VerificationCommandId, item.Execution.Id))
                .SequenceEqual(sourceInputs.OrderedVerification))
        {
            return ReadOnlyFormatRepairSource.InputsMismatch();
        }

        if (await CodeReviewInputIdentity.HasCompetingSuccessfulReviewAsync(
                dbContext,
                run.Id,
                attemptId: Guid.Empty,
                sealedExecutionReportMessageId,
                sealedVerification.Select(item => item.Execution.Id).ToArray(),
                cancellationToken))
        {
            return Error.Conflict(
                "agent_attempts.already_code_reviewed",
                "This exact implementation and verification evidence set already has a successful code review.");
        }

        return await dbContext.Attempts.AsNoTracking().AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken)
            ? Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.")
            : null;
    }

    private sealed record ExecutionReportValidation(
        ImplementerExecutionReportEligibility.Result? Value,
        Error? Error)
    {
        public static ExecutionReportValidation Succeeded(ImplementerExecutionReportEligibility.Result value) =>
            new(value, null);

        public static ExecutionReportValidation Failed(Error error) => new(null, error);
    }

    private sealed record VerificationEvidenceValidation(
        IReadOnlyList<(VerificationExecution Execution, string CommandName, int CommandNumber)>? OrderedExecutions, Error? Error)
    {
        public static VerificationEvidenceValidation Succeeded(
            IReadOnlyList<(VerificationExecution Execution, string CommandName, int CommandNumber)> orderedExecutions) =>
            new(orderedExecutions, null);

        public static VerificationEvidenceValidation Failed(Error error) => new(null, error);
    }
}
