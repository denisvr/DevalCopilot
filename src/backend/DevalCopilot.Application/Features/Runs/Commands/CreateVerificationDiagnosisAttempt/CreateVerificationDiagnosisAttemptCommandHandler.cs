using System.Data.Common;
using System.Text;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;

/// <summary>
/// Claims one durable Codex verification-diagnosis attempt (ADR-0018). It mirrors <c>CreateCodeReviewAttemptCommandHandler</c>'s
/// workspace/lease/checkpoint eligibility chain, sealed-manifest handling, and persistence-race protections exactly, with the
/// verification gate inverted: instead of requiring every enabled command to have Passed, the host derives the complete,
/// ordered selection and requires at least one coherent Failed/Exited/nonzero execution among otherwise coherent terminal
/// ones (<see cref="VerificationDiagnosisEvidence"/>), with the failed commands' sealed output verified and excerpted before
/// anything is claimed. The ExecutionReport, the true implemented plan (through the validated lineage), and the complete
/// ordered verification membership are pinned. After all external work, one short transaction re-reads every piece of
/// authority untracked — workspace, lease, current checkpoint, report chain, verification selection and its failed output,
/// competing diagnoses, and the running slot — beside the preference, stop-policy, and execution-mode guards, so a change
/// committed after the earlier checks is seen here rather than merely late-read. Every failure leaves nothing claimed and
/// removes the sealed manifest. It never accepts a format-repair source, an authorization, or caller-supplied evidence.
/// </summary>
public sealed class CreateVerificationDiagnosisAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe,
    IAccountUsageObserver? accountUsageGuardAdapter = null)
    : ICommandHandler<CreateVerificationDiagnosisAttemptCommand, Result<CreateVerificationDiagnosisAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    // The diagnosis uses the CodeReview timeout and permission profile; it is not a new claim path.
    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.CodeReview);

    public async Task<Result<CreateVerificationDiagnosisAttemptCommandResult>> HandleAsync(
        CreateVerificationDiagnosisAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Failure(executionModeError);
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start a verification diagnosis."));
        }

        if (await dbContext.Attempts.AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The run-wide Agent claim budget, the independent invocation-time budget, and the Codex token stop: the diagnosis
        // consumes exactly the budgets an ordinary code review does, checked before any probe, evidence capture, or sealing.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        var timeBudgetError = await CheckTimeBudgetAsync(run, asNoTracking: false, cancellationToken);
        if (timeBudgetError is not null)
        {
            return Failure(timeBudgetError);
        }

        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
        if (tokenStopError is not null)
        {
            return Failure(tokenStopError);
        }

        // The run-scoped Codex account-usage stop (ADR-0025), after the budgets and the token stop and before any provider probe, Git
        // work, or manifest sealing. The strict observation happens here, outside any EF transaction; a refusal commits nothing.
        // Unconfigured, it makes no observation. The commit seam below re-confirms the setting, the launch tuple and freshness.
        var accountUsageCheck = await CodexAccountUsageStopGate.CheckClaimAsync(
            dbContext, accountUsageGuardAdapter, timeProvider, run, cancellationToken);
        if (accountUsageCheck.IsFailure)
        {
            return Failure(accountUsageCheck.Errors[0]);
        }

        var accountUsageGuard = accountUsageCheck.Value;

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Failure(Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a verification diagnosis."));
        }

        if (!await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a verification diagnosis."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a verification diagnosis."));
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null || codexSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Failure(Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: true, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_not_current", "The selected result checkpoint is no longer current for this workspace."));
        }

        var reportValidation = await VerificationDiagnosisReportValidation.ValidateAsync(
            dbContext, run.Id, command.ExecutionReportMessageId, workspace.Id, checkpoint, asNoTracking: false, cancellationToken);
        if (reportValidation.Error is { } reportError)
        {
            return Failure(reportError);
        }

        var reportChain = reportValidation.Value!;
        var executionReport = reportChain.ExecutionReport;
        var implementedPlan = reportChain.ImplementedPlan;

        var selectionRead = await VerificationDiagnosisEvidence.ReadAsync(
            dbContext, run.ProjectId, workspace.Id, checkpoint, asNoTracking: false, cancellationToken);
        if (selectionRead.Error is { } selectionError)
        {
            return Failure(selectionError);
        }

        var selection = selectionRead.Value!;
        if (await VerificationDiagnosisInputIdentity.HasCompetingSuccessfulDiagnosisAsync(
                dbContext, run.Id, Guid.Empty, executionReport.Id, selection.OrderedExecutionIds, cancellationToken))
        {
            return Failure(Error.Conflict(
                "agent_attempts.already_diagnosed",
                "This exact implementation and verification evidence set already has a successful diagnosis."));
        }

        // The failed streams are verified through the artifact store and excerpted before anything is claimed; a missing or
        // unverifiable one refuses the claim.
        var prefixes = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(artifactStore, selection, cancellationToken);
        if (prefixes.Error is { } prefixError)
        {
            return Failure(prefixError);
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestJson = VerificationDiagnosisContextManifestBuilder.Build(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            implementedPlan.Id,
            implementedPlan.Summary,
            implementedPlan.StructuredContentJson,
            executionReport.Id,
            executionReport.Summary,
            executionReport.StructuredContentJson,
            selection,
            prefixes.Streams!,
            evidence.ChangedPaths,
            TrackedChangeEvidence.From(evidence),
            ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext),
            evidence.UntrackedFiles);
        if (Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

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
        var (requestedModel, requestedEffort) = await CurrentCodexAssignmentPreference.ReadAsync(dbContext, run.Id, cancellationToken);

        var attempt = Attempt.ClaimAgentVerificationDiagnosis(
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
            agentBudgetSlot);
        CodexAccountUsageStopGate.Snapshot(attempt, accountUsageGuard);

        // The one authoritative record of this attempt's exact durable identity: the diagnosed ExecutionReport at sequence 0
        // and the exact ordered claimed verification-execution set — one relational membership row per execution.
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReport.Id, sequence: 0);
        var verificationEvidenceRows = selection.Entries
            .Select((entry, index) => AttemptVerificationEvidence.RecordDiagnosisSnapshot(
                Guid.NewGuid(), attemptId, entry.Execution.VerificationCommandId, entry.Execution.Id, sequence: index,
                VerificationDiagnosisSnapshot.Compute(entry)))
            .ToList();
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

        IDbContextTransaction claimTransaction;
        try
        {
            claimTransaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            throw;
        }
        catch (DbException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Failure(Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            bool preferenceStillCurrent;
            bool stopPolicyStillCurrent;
            Error? accountUsageError;
            bool modeStillAdmitted;
            Error? authorityError = null;
            try
            {
                preferenceStillCurrent = await CurrentCodexAssignmentPreference.ConfirmUnchangedAsync(
                    dbContext, run.Id, requestedModel, requestedEffort, cancellationToken);
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);

                // The account-usage stop (ADR-0025): the stored setting and the vetted launch tuple must still be exactly what the
                // early check observed, and its evidence still fresh at this seam, before any insert.
                accountUsageError = await CodexAccountUsageStopGate.ConfirmInTransactionAsync(
                    dbContext, timeProvider, run, accountUsageGuard, cancellationToken);
                modeStillAdmitted = await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, cancellationToken);

                // Authority is re-read here, untracked, after the guard writes above hold the database write lock and after
                // all external work, so it is atomic with the insert below.
                if (preferenceStillCurrent && stopPolicyStillCurrent && accountUsageError is null && modeStillAdmitted)
                {
                    authorityError = await RevalidateAuthorityAsync(
                        run, workspace.Id, checkpoint, executionReport.Id, implementedPlan.Id, selection, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Failure(Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (!modeStillAdmitted)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, CurrentRunExecutionMode.NotAdmitted(), cancellationToken);
            }

            if (!preferenceStillCurrent)
            {
                return await RefuseAsync(
                    claimTransaction,
                    run.Id,
                    attemptId,
                    Error.Conflict(
                        "agent_attempts.assignment_preference_changed",
                        "The run's requested Codex model or effort changed while this claim was being prepared; retry the request."),
                    cancellationToken);
            }

            if (!stopPolicyStillCurrent)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, CurrentTokenStopPolicy.PolicyChangedDuringClaim(), cancellationToken);
            }

            if (accountUsageError is not null)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, accountUsageError, cancellationToken);
            }

            if (authorityError is not null)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, authorityError, cancellationToken);
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
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                await CleanupIfNotPersistedAsync(run.Id, attemptId, attempt, inputMessage, verificationEvidenceRows, manifestArtifact, CancellationToken.None);
                throw;
            }
            catch (DbUpdateException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateVerificationDiagnosisAttemptCommandResult>.Success(
                        new CreateVerificationDiagnosisAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Failure(Error.Failure(
                        "attempts.persistence_unresolved", "Whether the attempt was durably recorded could not be confirmed."));
                }

                RemoveTracked(attempt, inputMessage, verificationEvidenceRows, manifestArtifact);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return await ClassifyClaimRaceAsync(run, agentBudgetSlot, cancellationToken);
            }

            try
            {
                await claimTransaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                await CleanupIfNotPersistedAsync(run.Id, attemptId, attempt, inputMessage, verificationEvidenceRows, manifestArtifact, CancellationToken.None);
                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateVerificationDiagnosisAttemptCommandResult>.Success(
                        new CreateVerificationDiagnosisAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Failure(Error.Failure(
                        "attempts.persistence_unresolved", "Whether the attempt was durably recorded could not be confirmed."));
                }

                RemoveTracked(attempt, inputMessage, verificationEvidenceRows, manifestArtifact);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Failure(Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }
        }

        return Result<CreateVerificationDiagnosisAttemptCommandResult>.Success(
            new CreateVerificationDiagnosisAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>The claim's final in-transaction read, decided against the state the write lock now protects. Everything is
    /// read untracked: the workspace, lease, and current checkpoint still are the ones the manifest was sealed against; the
    /// report chain still resolves with the same ImplementedPlan; the verification selection and its sealed failed output are
    /// the very ones sealed; no competing diagnosis exists; and no attempt is Running.</summary>
    private async Task<Error?> RevalidateAuthorityAsync(
        Run run,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid sealedReportId,
        Guid sealedPlanId,
        VerificationDiagnosisEvidence.Selection sealedSelection,
        CancellationToken cancellationToken)
    {
        var lifecycle = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => (RunLifecycle?)candidate.Lifecycle)
            .SingleOrDefaultAsync(cancellationToken);
        var workspaceReady = await dbContext.GitWorkspaces.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == workspaceId && candidate.Status == WorkspaceStatus.Ready, cancellationToken);
        var leaseActive = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .AnyAsync(lease => lease.WorkspaceId == workspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        var currentCheckpointId = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspaceId)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (lifecycle != RunLifecycle.Running || !workspaceReady || !leaseActive || currentCheckpointId != checkpoint.Id)
        {
            return Error.Conflict("agent_attempts.checkpoint_not_current", "The selected result checkpoint is no longer current for this workspace.");
        }

        try
        {
            var report = await VerificationDiagnosisReportValidation.ValidateAsync(
                dbContext, run.Id, sealedReportId, workspaceId, checkpoint, asNoTracking: true, cancellationToken);
            if (report.Value is not { } chain || chain.ImplementedPlan.Id != sealedPlanId)
            {
                return Error.Conflict(
                    "agent_attempts.implementer_attempt_not_valid", "The execution report's Implementer result chain is no longer valid for a diagnosis.");
            }

            var selection = await VerificationDiagnosisEvidence.ReadAsync(
                dbContext, run.ProjectId, workspaceId, checkpoint, asNoTracking: true, cancellationToken);
            if (selection.Error is { } selectionError)
            {
                return selectionError;
            }

            if (!selection.Value!.SameAs(sealedSelection))
            {
                return Error.Conflict(
                    VerificationDiagnosisEvidence.NotDiagnosableCode,
                    "The verification evidence changed while this diagnosis was being prepared; retry the request.");
            }
        }
        catch (InvalidOperationException)
        {
            // A persisted row could not be materialized (typically an unparseable stored enum string): fixed refusal.
            return Error.Conflict("agent_attempts.implementer_attempt_not_valid", "The execution report's Implementer result chain is not valid for a diagnosis.");
        }

        if (await VerificationDiagnosisInputIdentity.HasCompetingSuccessfulDiagnosisAsync(
                dbContext, run.Id, Guid.Empty, sealedReportId, sealedSelection.OrderedExecutionIds, cancellationToken))
        {
            return Error.Conflict(
                "agent_attempts.already_diagnosed", "This exact implementation and verification evidence set already has a successful diagnosis.");
        }

        return await dbContext.Attempts.AsNoTracking().AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken)
            ? Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.")
            : null;
    }

    private async Task<Result<CreateVerificationDiagnosisAttemptCommandResult>> RefuseAsync(
        IDbContextTransaction transaction, Guid runId, Guid attemptId, Error error, CancellationToken cancellationToken)
    {
        // Rolled back before any insert: nothing is claimed or consumed, and the sealed manifest is removed.
        await RollbackBestEffortAsync(transaction, cancellationToken);
        artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        return Failure(error);
    }

    private async Task CleanupIfNotPersistedAsync(
        Guid runId,
        Guid attemptId,
        Attempt attempt,
        AttemptInputMessage inputMessage,
        List<AttemptVerificationEvidence> verificationEvidenceRows,
        Artifact manifestArtifact,
        CancellationToken cancellationToken)
    {
        // The database may have applied the insert before cancellation was observed; a fresh, untracked read is the sole
        // authority for the sealed manifest's fate. Persisted or Unresolved keeps the file.
        var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
        if (durability == AttemptDurabilityCheckResult.NotPersisted)
        {
            artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
            RemoveTracked(attempt, inputMessage, verificationEvidenceRows, manifestArtifact);
        }
    }

    private void RemoveTracked(
        Attempt attempt, AttemptInputMessage inputMessage, List<AttemptVerificationEvidence> verificationEvidenceRows, Artifact manifestArtifact)
    {
        dbContext.Attempts.Remove(attempt);
        dbContext.AttemptInputMessages.Remove(inputMessage);
        foreach (var row in verificationEvidenceRows)
        {
            dbContext.AttemptVerificationEvidence.Remove(row);
        }

        dbContext.Artifacts.Remove(manifestArtifact);
    }

    private async Task<Result<CreateVerificationDiagnosisAttemptCommandResult>> ClassifyClaimRaceAsync(
        Run run, int agentBudgetSlot, CancellationToken cancellationToken)
    {
        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The race the (RunId, AgentBudgetSlot) unique index closes: below the maximum it is a retryable conflict; at the
        // maximum it is truthfully exhaustion; a concurrent claim can also have pushed the time budget or token stop over.
        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                cancellationToken))
        {
            var used = await dbContext.Attempts.AsNoTracking().CountAsync(
                candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
            if (used >= run.MaximumAgentAttempts)
            {
                return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
            }

            var timeBudgetError = await CheckTimeBudgetAsync(run, asNoTracking: true, cancellationToken);
            if (timeBudgetError is not null)
            {
                return Failure(timeBudgetError);
            }

            var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
            if (tokenStopOnRace is not null)
            {
                return Failure(tokenStopOnRace);
            }

            return Failure(Error.Conflict(
                "agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
        }

        return Failure(Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
    }

    private async Task<Error?> CheckTimeBudgetAsync(Run run, bool asNoTracking, CancellationToken cancellationToken)
    {
        if (run.MaximumAgentInvocationTime is not { } maximum)
        {
            return null;
        }

        const string invalid =
            "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable.";
        var reserved = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking, cancellationToken);
        if (reserved is null)
        {
            return Error.Failure("agent_attempts.time_budget_evidence_invalid", invalid);
        }

        var projected = AgentInvocationTimeReservation.ComputeProjectedReservation(reserved.Value, InvocationTimeout);
        if (projected is null)
        {
            return Error.Failure("agent_attempts.time_budget_evidence_invalid", invalid);
        }

        return projected.Value > maximum
            ? Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time.")
            : null;
    }

    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A rollback attempted after an ambiguous or already-completed commit can throw; the durability probe each caller
            // runs afterwards is the authority on durable state.
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception)
            {
                // Best-effort release; the enclosing `await using` disposes again as a safe no-op.
            }
        }
    }

    private static Result<CreateVerificationDiagnosisAttemptCommandResult> Failure(Error error) =>
        Result<CreateVerificationDiagnosisAttemptCommandResult>.Failure(error);
}
