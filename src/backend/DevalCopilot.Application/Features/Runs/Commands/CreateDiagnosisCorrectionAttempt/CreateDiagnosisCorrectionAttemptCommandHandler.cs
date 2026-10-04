using System.Data.Common;
using System.Text;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;

/// <summary>
/// Claims a correction only from a completed, exactly applicable verification diagnosis that recorded findings (ADR-0018).
/// It follows <c>CreateReviewCorrectionAttemptCommandHandler</c> — the same run-wide Agent count and reserved-time budgets,
/// Claude token stop, CodeReview-independent ReviewCorrection timeout, current Claude model/effort/turn-limit requests, sealed
/// manifest, and ordered inputs (the previous report, then every finding in timeline order) — with three differences: the
/// source is the diagnosis (its structure, its complete findings, and the unchanged current verification membership are
/// re-read untracked, once before sealing and again — together with the lifecycle, workspace, lease, checkpoint, run-wide gates, and
/// shared allowance — inside the short claim transaction, after all external work and under the database write lock), the manifest carries the fixed diagnosis source
/// notice, and exhaustion of the shared correction budget records one durable idempotent Orchestrator escalation bound to the
/// diagnosis with no Attempt. There is no authorization for this source: the ordinary review's extra correction grants cannot be
/// transferred to it. Optional advisory direct human guidance (ADR-0015, extended to this request by ADR-0019) is normalized before
/// any read, snapshotted on the claimed attempt and sealed once into the existing manifest envelope, and is available only within
/// the shared allowance: at exhaustion a guided request is refused whole (before sealing and again at the locked claim seam, with an
/// unused seal removed) while an unguided request keeps its idempotent escalation.
/// </summary>
public sealed class CreateDiagnosisCorrectionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<CreateDiagnosisCorrectionAttemptCommand, Result<CreateDiagnosisCorrectionAttemptCommandResult>>
{
    public const string NotApplicableCode = "agent_attempts.diagnosis_not_applicable";

    private const int MaxContextManifestBytes = 32 * 1024;
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;
    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.ReviewCorrection);

    public async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> HandleAsync(
        CreateDiagnosisCorrectionAttemptCommand command, CancellationToken cancellationToken)
    {
        // Supplied direct guidance is normalized (and refused if invalid) before any read or external work. The validator
        // normally rejects it first; this keeps a direct handler call equally safe. Null stays unguided.
        string? directGuidance = null;
        if (command.Guidance is not null)
        {
            directGuidance = DirectHumanGuidance.Normalize(command.Guidance);
            if (directGuidance is null)
            {
                return Failure(DirectHumanGuidanceErrors.Invalid());
            }
        }

        // Phase 1 — external work, outside any transaction: cheap early refusals, Git evidence, the first untracked resolution
        // of the diagnosis, and the sealed manifest. Nothing here is authority; everything is decided again in phase 2.
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
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        var earlyError = await CheckBudgetsAndActivityAsync(run, asNoTracking: false, cancellationToken);
        if (earlyError is not null)
        {
            return Failure(earlyError);
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

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: true, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var current = await ResolveCurrentDiagnosisAsync(run.Id, command.VerificationDiagnosisAttemptId, workspace.Id, checkpoint.Id, cancellationToken);
        if (current is null)
        {
            return Failure(NotApplicable());
        }

        var sealedReportId = current.ExecutionReport.Id;
        var sealedFindingIds = current.OrderedFindings.Select(finding => finding.Id).ToArray();
        var sealedDiagnosisId = current.DiagnosisAttempt.Id;

        var exhaustedBeforeSealing = await CorrectionAllowanceSpentAsync(run, cancellationToken);
        if (exhaustedBeforeSealing)
        {
            // Direct guidance exists only within the shared allowance: at exhaustion the whole guided request is refused, after
            // every authority gate above and before any escalation is created or anything is sealed or claimed.
            if (directGuidance is not null)
            {
                return Failure(DirectHumanGuidanceErrors.UnavailableAtExhaustion());
            }

            // The shared allowance is spent: one durable idempotent human-attention fact bound to this diagnosis, no Attempt,
            // no sealed manifest, nothing consumed. Its authority is decided in the same short transaction as the insert.
            return await RecordEscalationAsync(
                run, workspace.Id, checkpoint, sealedDiagnosisId, sealedReportId, sealedFindingIds, cancellationToken);
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (!ClaudeObserved(claudeSnapshot))
        {
            return Failure(Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var attemptId = Guid.NewGuid();
        if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext, run.Id, attemptId, checkpoint.Id, [sealedReportId, .. sealedFindingIds], cancellationToken))
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
            current.ExecutionReport.Id,
            current.ExecutionReport.Summary,
            current.ExecutionReport.StructuredContentJson,
            current.OrderedFindings.Select(finding => new ReviewCorrectionContextManifestBuilder.Finding(
                finding.Id, finding.Summary, finding.StructuredContentJson)).ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff,
            ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext),
            humanGuidance: null,
            untrackedFiles: evidence.UntrackedFiles,
            directHumanGuidance: directGuidance,
            sourceNotice: ReviewCorrectionContextManifestBuilder.VerificationDiagnosisSourceNotice);
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

        // Phase 2 — one short transaction. The write lock is taken first (guarded no-op writes of the run's execution mode and
        // token-stop policy), then every piece of authority is re-read untracked under it, and the claim inserts, saves, and
        // commits under the same lock.
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
            bool modeStillAdmitted;
            bool stopPolicyStillCurrent;
            ClaudeModelRequestSnapshot requestedClaude = default!;
            Result<int?> requestedTurnLimit = default!;
            Error? authorityError = null;
            Authority? authority = null;
            try
            {
                modeStillAdmitted = await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, cancellationToken);
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);
                if (modeStillAdmitted && stopPolicyStillCurrent)
                {
                    requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);
                    requestedTurnLimit = await CurrentClaudeMutationTurnLimit.ReadAndGuardAsync(dbContext, run, cancellationToken);
                    if (requestedTurnLimit.IsSuccess)
                    {
                        (authority, authorityError) = await RevalidateAuthorityAsync(
                            run, workspace.Id, checkpoint, sealedDiagnosisId, sealedReportId, sealedFindingIds, cancellationToken);
                    }
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

            if (!stopPolicyStillCurrent)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, CurrentTokenStopPolicy.PolicyChangedDuringClaim(), cancellationToken);
            }

            if (requestedTurnLimit.IsFailure)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, requestedTurnLimit.Errors[0], cancellationToken);
            }

            if (authorityError is not null)
            {
                return await RefuseAsync(claimTransaction, run.Id, attemptId, authorityError, cancellationToken);
            }

            if (authority!.AllowanceSpent && directGuidance is not null)
            {
                // The allowance was spent between phase 1 and the lock: direct guidance is not available past it, so the guided
                // request is refused whole (nothing claimed, no escalation) and the unused manifest is removed.
                return await RefuseAsync(
                    claimTransaction, run.Id, attemptId, DirectHumanGuidanceErrors.UnavailableAtExhaustion(), cancellationToken);
            }

            if (authority.AllowanceSpent)
            {
                // The allowance was spent between phase 1 and the lock: this request records the escalation instead, in a fresh
                // transaction of its own, and the unused manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return await RecordEscalationAsync(
                    run, workspace.Id, checkpoint, sealedDiagnosisId, sealedReportId, sealedFindingIds, cancellationToken);
            }

            var nowUtc = timeProvider.GetUtcNow();
            var attemptNumber = authority.AttemptCount + 1;
            var agentBudgetSlot = authority.AgentAttemptsUsed + 1;
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

            var inputRows = new List<AttemptInputMessage>(sealedFindingIds.Length + 1)
            {
                AttemptInputMessage.Record(Guid.NewGuid(), attemptId, sealedReportId, 0),
            };
            inputRows.AddRange(sealedFindingIds.Select((findingId, index) => AttemptInputMessage.Record(
                Guid.NewGuid(), attemptId, findingId, index + 1)));
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
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.AddRange(inputRows);
            dbContext.Artifacts.Add(manifestArtifact);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                await CleanupIfNotPersistedAsync(run.Id, attemptId, attempt, inputRows, manifestArtifact, CancellationToken.None);
                throw;
            }
            catch (DbUpdateException exception)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateDiagnosisCorrectionAttemptCommandResult>.Success(
                        new CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Failure(Error.Failure(
                        "attempts.persistence_unresolved", "Whether the attempt was durably recorded could not be confirmed."));
                }

                RemoveTracked(attempt, inputRows, manifestArtifact);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                if (exception is DbUpdateConcurrencyException)
                {
                    return Failure(
                        await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                            ? CurrentRunExecutionMode.ChangedDuringClaim()
                            : await CurrentTokenStopPolicy.HasChangedAsync(dbContext, run, cancellationToken)
                            ? CurrentTokenStopPolicy.PolicyChangedDuringClaim()
                            : CurrentClaudeModelPreference.RunChangedDuringClaim());
                }

                return await ClassifyClaimRaceAsync(run, agentBudgetSlot, cancellationToken);
            }

            try
            {
                await claimTransaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                await CleanupIfNotPersistedAsync(run.Id, attemptId, attempt, inputRows, manifestArtifact, CancellationToken.None);
                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateDiagnosisCorrectionAttemptCommandResult>.Success(
                        new CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Failure(Error.Failure(
                        "attempts.persistence_unresolved", "Whether the attempt was durably recorded could not be confirmed."));
                }

                RemoveTracked(attempt, inputRows, manifestArtifact);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Failure(Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            return Result<CreateDiagnosisCorrectionAttemptCommandResult>.Success(
                new CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated(attempt.Id, attempt.AttemptNumber));
        }
    }

    /// <summary>The facts the in-transaction revalidation read, used to place the claim.</summary>
    private sealed record Authority(int AttemptCount, int AgentAttemptsUsed, bool AllowanceSpent);

    private static bool ClaudeObserved(HostCapabilitySnapshot? snapshot) =>
        snapshot is not null
        && snapshot.ReasonCode == CapabilityProbeReason.None
        && !string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath);

    private async Task<bool> CorrectionAllowanceSpentAsync(Run run, CancellationToken cancellationToken)
    {
        var used = await dbContext.Attempts.AsNoTracking().CountAsync(
            candidate => candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection,
            cancellationToken);
        return used >= run.MaximumReviewCorrectionAttempts;
    }

    /// <summary>The run-wide gates every branch passes: no active attempt, the Agent attempt count, the reserved-time budget,
    /// and the Claude token stop.</summary>
    private async Task<Error?> CheckBudgetsAndActivityAsync(Run run, bool asNoTracking, CancellationToken cancellationToken)
    {
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

        var timeBudgetError = await CheckTimeBudgetAsync(run, asNoTracking, cancellationToken);
        if (timeBudgetError is not null)
        {
            return timeBudgetError;
        }

        return await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
    }

    /// <summary>The claim's final in-transaction read, decided against the state the write lock now protects. Everything is
    /// read untracked: lifecycle, workspace, lease, and current checkpoint; the run-wide gates; the named diagnosis with its
    /// report chain, complete findings, and unchanged verification membership; a competing correction; and the Claude runtime.</summary>
    private async Task<(Authority? Authority, Error? Error)> RevalidateAuthorityAsync(
        Run run,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid diagnosisId,
        Guid sealedReportId,
        IReadOnlyList<Guid> sealedFindingIds,
        CancellationToken cancellationToken)
    {
        var lifecycle = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => (RunLifecycle?)candidate.Lifecycle)
            .SingleOrDefaultAsync(cancellationToken);
        if (lifecycle != RunLifecycle.Running)
        {
            return (null, Error.Conflict("runs.not_running", "The run is not active."));
        }

        var workspaceReady = await dbContext.GitWorkspaces.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == workspaceId && candidate.Status == WorkspaceStatus.Ready, cancellationToken);
        var leaseActive = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .AnyAsync(lease => lease.WorkspaceId == workspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
        var currentCheckpointId = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspaceId)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!workspaceReady || !leaseActive || currentCheckpointId != checkpoint.Id)
        {
            return (null, Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var gateError = await CheckBudgetsAndActivityAsync(run, asNoTracking: true, cancellationToken);
        if (gateError is not null)
        {
            return (null, gateError);
        }

        var still = await ResolveCurrentDiagnosisAsync(run.Id, diagnosisId, workspaceId, checkpoint.Id, cancellationToken);
        if (still is null
            || still.ExecutionReport.Id != sealedReportId
            || !still.OrderedFindings.Select(finding => finding.Id).SequenceEqual(sealedFindingIds))
        {
            return (null, NotApplicable());
        }

        var spent = await CorrectionAllowanceSpentAsync(run, cancellationToken);
        if (!spent)
        {
            var claudeSnapshot = await dbContext.HostCapabilitySnapshots.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
            if (!ClaudeObserved(claudeSnapshot))
            {
                return (null, Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
            }

            if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                    dbContext, run.Id, Guid.Empty, checkpoint.Id, [sealedReportId, .. sealedFindingIds], cancellationToken))
            {
                return (null, Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction."));
            }
        }

        var attemptCount = await dbContext.Attempts.AsNoTracking().CountAsync(candidate => candidate.RunId == run.Id, cancellationToken);
        var agentAttemptsUsed = await dbContext.Attempts.AsNoTracking().CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
        return (new Authority(attemptCount, agentAttemptsUsed, spent), null);
    }

    private async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> RefuseAsync(
        IDbContextTransaction transaction, Guid runId, Guid attemptId, Error error, CancellationToken cancellationToken)
    {
        // Rolled back before any insert: nothing is claimed or consumed, and the sealed manifest is removed.
        await RollbackBestEffortAsync(transaction, cancellationToken);
        artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        return Failure(error);
    }

    private async Task CleanupIfNotPersistedAsync(
        Guid runId, Guid attemptId, Attempt attempt, List<AttemptInputMessage> inputRows, Artifact manifestArtifact,
        CancellationToken cancellationToken)
    {
        // The database may have applied the insert before cancellation was observed; a fresh, untracked read is the sole
        // authority for the sealed manifest's fate. Persisted or Unresolved keeps the file.
        var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
        if (durability == AttemptDurabilityCheckResult.NotPersisted)
        {
            artifactStore.DeleteOrphanedSealedFile(runId, attemptId, ArtifactPurpose.AgentContextManifest);
            RemoveTracked(attempt, inputRows, manifestArtifact);
        }
    }

    private void RemoveTracked(Attempt attempt, List<AttemptInputMessage> inputRows, Artifact manifestArtifact)
    {
        dbContext.Attempts.Remove(attempt);
        foreach (var inputMessage in inputRows)
        {
            dbContext.AttemptInputMessages.Remove(inputMessage);
        }

        dbContext.Artifacts.Remove(manifestArtifact);
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

    /// <summary>The named diagnosis resolved against the current checkpoint, read untracked: it is a completed findings
    /// diagnosis whose report chain, complete findings, and verification evidence still exactly apply.</summary>
    private async Task<VerificationDiagnosisEligibility.CurrentDiagnosis?> ResolveCurrentDiagnosisAsync(
        Guid runId, Guid diagnosisAttemptId, Guid workspaceId, Guid checkpointId, CancellationToken cancellationToken)
    {
        try
        {
            var diagnosis = await dbContext.Attempts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == diagnosisAttemptId && candidate.RunId == runId, cancellationToken);
            if (diagnosis is null)
            {
                return null;
            }

            var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, runId, cancellationToken);
            var resolved = VerificationDiagnosisEligibility.ResolveForAttempt(snapshot, diagnosis, runId, workspaceId, checkpointId);
            if (resolved is null
                || resolved.OrderedFindings.Count > ReviewCorrectionOutputSchema.MaximumFindings
                || await VerificationDiagnosisApplicability.EvaluateAsync(dbContext, diagnosis, cancellationToken)
                    != VerificationDiagnosisApplicability.Verdict.Applicable)
            {
                return null;
            }

            return resolved;
        }
        catch (InvalidOperationException)
        {
            // A persisted row could not be materialized (typically an unparseable stored enum string): fixed refusal.
            return null;
        }
    }

    private async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> ClassifyClaimRaceAsync(
        Run run, int agentBudgetSlot, CancellationToken cancellationToken)
    {
        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

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

            var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
            if (tokenStopOnRace is not null)
            {
                return Failure(tokenStopOnRace);
            }

            return Failure(Error.Conflict(
                "agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
        }

        return Failure(Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded."));
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

    /// <summary>Records (or returns) the diagnosis's one durable escalation in its own short transaction: the write lock is
    /// taken, the same authority a claim needs is re-read untracked under it — so drift committed just before the transaction
    /// begins records no stale escalation — and the message, escalation row, and event commit together.</summary>
    private async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> RecordEscalationAsync(
        Run run,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid diagnosisId,
        Guid sealedReportId,
        IReadOnlyList<Guid> sealedFindingIds,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction;
        try
        {
            transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (DbException)
        {
            return Failure(Error.Failure("attempts.persistence_failed", "The escalation could not be durably recorded."));
        }

        await using (transaction)
        {
            bool modeStillAdmitted;
            bool stopPolicyStillCurrent;
            Authority? authority = null;
            Error? authorityError = null;
            DiagnosisCorrectionEscalation? existing = null;
            CollaborationMessage? report = null;
            try
            {
                modeStillAdmitted = await CurrentRunExecutionMode.ConfirmAgentAdmittedAsync(dbContext, run, cancellationToken);
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);
                if (modeStillAdmitted && stopPolicyStillCurrent)
                {
                    (authority, authorityError) = await RevalidateAuthorityAsync(
                        run, workspaceId, checkpoint, diagnosisId, sealedReportId, sealedFindingIds, cancellationToken);
                    if (authorityError is null)
                    {
                        existing = await dbContext.DiagnosisCorrectionEscalations.AsNoTracking()
                            .SingleOrDefaultAsync(candidate => candidate.VerificationDiagnosisAttemptId == diagnosisId, cancellationToken);
                        report = await dbContext.CollaborationMessages.AsNoTracking()
                            .SingleOrDefaultAsync(message => message.Id == sealedReportId, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(transaction, CancellationToken.None);
                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return Failure(Error.Failure("attempts.persistence_failed", "The escalation could not be durably recorded."));
            }

            if (!modeStillAdmitted)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return Failure(CurrentRunExecutionMode.NotAdmitted());
            }

            if (!stopPolicyStillCurrent)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return Failure(CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            if (authorityError is not null)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return Failure(authorityError);
            }

            if (existing is not null)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return await ExistingEscalationAsync(run.Id, existing, cancellationToken);
            }

            if (!authority!.AllowanceSpent || report is null)
            {
                // The allowance is not spent after all (nothing to escalate); retrying re-evaluates as a correction claim.
                await RollbackBestEffortAsync(transaction, cancellationToken);
                return Failure(Error.Conflict(
                    "agent_attempts.budget_slot_conflict", "The correction allowance changed while this request was being prepared; retry the request."));
            }

            var nowUtc = timeProvider.GetUtcNow();
            var message = CollaborationMessage.Record(
                Guid.NewGuid(),
                run.Id,
                null,
                CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForOrchestrator(),
                ParticipantIdentity.ForHuman(),
                CollaborationMessageType.Escalation,
                report.Id,
                "Correcting the diagnosed verification failure requires an explicit human decision.",
                "{\"unresolvedDecision\":\"The verification diagnosis recorded findings, but the shared correction allowance is spent.\",\"options\":\"Review the diagnosis and the verification output manually and record an explicit human decision. This source offers no further automated correction.\",\"consequences\":\"No further correction is claimed from this diagnosis until a human acts; the run stays awaiting human action.\",\"evidence\":\"The configured review-correction attempt limit was reached.\",\"recommendedChoice\":\"Stop unless a further correction is explicitly justified.\"}",
                CollaborationMessageProvenance.HostConstructed,
                nowUtc);
            var escalation = DiagnosisCorrectionEscalation.Record(Guid.NewGuid(), run.Id, diagnosisId, message.Id, nowUtc);
            var escalationEvent = RunEvent.Record(
                Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, message.Actor,
                "{\"messageId\":\"" + message.Id + "\",\"type\":\"Escalation\",\"provenance\":\"HostConstructed\"}", nowUtc);
            dbContext.CollaborationMessages.Add(message);
            dbContext.DiagnosisCorrectionEscalations.Add(escalation);
            dbContext.Events.Add(escalationEvent);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(transaction, CancellationToken.None);
                throw;
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException)
            {
                await RollbackBestEffortAsync(transaction, cancellationToken);
                dbContext.DiagnosisCorrectionEscalations.Remove(escalation);
                dbContext.CollaborationMessages.Remove(message);
                dbContext.Events.Remove(escalationEvent);
                return await ClassifyEscalationFailureAsync(run.Id, diagnosisId, cancellationToken);
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, escalationEvent.Sequence, cancellationToken);
            }

            return Result<CreateDiagnosisCorrectionAttemptCommandResult>.Success(
                new CreateDiagnosisCorrectionAttemptCommandResult.Escalated(escalation.Id, message.Id, escalationEvent.Sequence));
        }
    }

    /// <summary>After a failed escalation insert or commit: the escalation either exists now (a concurrent or ambiguous commit
    /// that did persist — idempotently returned), provably does not, or cannot be confirmed (truthfully unresolved).</summary>
    private async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> ClassifyEscalationFailureAsync(
        Guid runId, Guid diagnosisId, CancellationToken cancellationToken)
    {
        try
        {
            var persisted = await dbContext.DiagnosisCorrectionEscalations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.VerificationDiagnosisAttemptId == diagnosisId, cancellationToken);
            return persisted is not null
                ? await ExistingEscalationAsync(runId, persisted, cancellationToken)
                : Failure(Error.Failure("attempts.persistence_failed", "The escalation could not be durably recorded."));
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException)
        {
            return Failure(Error.Failure(
                "attempts.persistence_unresolved", "Whether the escalation was durably recorded could not be confirmed."));
        }
    }

    private async Task<Result<CreateDiagnosisCorrectionAttemptCommandResult>> ExistingEscalationAsync(
        Guid runId, DiagnosisCorrectionEscalation escalation, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(escalation.CollaborationMessageId.ToString()))
            .Select(candidate => (long?)candidate.Sequence)
            .SingleOrDefaultAsync(cancellationToken);
        if (sequence is not { } latest)
        {
            return Failure(Error.Failure(
                "diagnosis_correction_escalations.event_missing", "The persisted escalation event could not be recovered."));
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, latest, cancellationToken);
        }

        return Result<CreateDiagnosisCorrectionAttemptCommandResult>.Success(
            new CreateDiagnosisCorrectionAttemptCommandResult.Escalated(escalation.Id, escalation.CollaborationMessageId, latest));
    }

    private static Error NotApplicable() =>
        Error.Conflict(NotApplicableCode, "The selected verification diagnosis is not an applicable findings diagnosis of the current verification.");

    private static Result<CreateDiagnosisCorrectionAttemptCommandResult> Failure(Error error) =>
        Result<CreateDiagnosisCorrectionAttemptCommandResult>.Failure(error);
}
