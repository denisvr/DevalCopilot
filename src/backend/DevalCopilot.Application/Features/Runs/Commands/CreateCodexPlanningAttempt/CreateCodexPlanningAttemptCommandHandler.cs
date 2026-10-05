using System.Data.Common;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

public sealed class CreateCodexPlanningAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe,
    IAccountUsageObserver? accountUsageGuardAdapter = null)
    : ICommandHandler<CreateCodexPlanningAttemptCommand, Result<CreateCodexPlanningAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 16 * 1024;

    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.CodexPlanning);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateCodexPlanningAttemptCommandResult>> HandleAsync(
        CreateCodexPlanningAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // The execution mode is a durable, immutable admission fact: read afresh (never from the tracked Run)
        // and checked before any workspace, evidence, manifest, or provider work.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(executionModeError);
        }

        if (run.Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_active", $"The run is {run.Lifecycle} and cannot start a planning attempt."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a Codex plan."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a Codex plan."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a Codex plan."));
        }

        // A manual format repair additionally requires an eligible source (see
        // PlanningRepairSource); it is re-evaluated at the durable claim boundary below. It is
        // evaluated before the running-attempt check so a request that lost a race to the
        // source's one repair is told the repair was already requested, not that some attempt is
        // running. An ordinary request skips this entirely and is unaffected.
        if (command.RepairSourceAttemptId is { } repairSourceAttemptId)
        {
            var sourceError = await PlanningRepairSource.EvaluateAsync(
                dbContext, run.Id, repairSourceAttemptId, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, cancellationToken);
            if (sourceError is not null)
            {
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(sourceError);
            }
        }

        // Run-wide, not Agent-scoped: a Simulated or Process attempt already Running for this run
        // is exactly as disqualifying as an Agent attempt already Running — only one attempt of
        // any kind may ever be Running for a run at a time. The filtered unique index on
        // (RunId WHERE Status = 'Running') is the database backstop for the race this check
        // alone cannot close.
        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and before any provider-availability probe, Git work, or
        // manifest sealing. Unconfigured, it reads nothing and changes nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
        if (tokenStopError is not null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(tokenStopError);
        }

        // The run-scoped Codex account-usage stop (ADR-0025), after the budgets and the token stop and before any provider
        // probe, Git work, or manifest sealing. The strict observation happens here, outside any EF transaction; a refusal commits
        // nothing. Unconfigured, it makes no observation. The commit seam below re-confirms the setting, the launch tuple and freshness.
        var accountUsageCheck = await CodexAccountUsageStopGate.CheckClaimAsync(
            dbContext, accountUsageGuardAdapter, timeProvider, run, cancellationToken);
        if (accountUsageCheck.IsFailure)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(accountUsageCheck.Errors);
        }

        var accountUsageGuard = accountUsageCheck.Value;

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null
            || codexSnapshot.ReasonCode != CapabilityProbeReason.None
            || codexSnapshot.LaunchKind is null
            || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: false, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var instructions = ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext);
        var manifestJson = command.RepairSourceAttemptId is null
            ? ContextManifestBuilder.Build(
                run.ProjectId, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, run.Objective, null, [], instructions)
            : ContextManifestBuilder.BuildFormatRepair(
                run.ProjectId, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, run.Objective, instructions);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields, but never silently
            // truncated or persisted over the bound if a future field addition regresses this.
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        // The final durable claim boundary: a genuinely fresh, untracked read of the Run's own
        // current preference, taken only now — after the external Git evidence capture and
        // artifact-sealing work above have both already completed. See CurrentCodexAssignmentPreference.
        var (requestedModel, requestedEffort) = await CurrentCodexAssignmentPreference.ReadAsync(dbContext, run.Id, cancellationToken);

        var attempt = command.RepairSourceAttemptId is { } sourceAttemptId
            ? Attempt.ClaimAgentPlanningRepair(
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
                sourceAttemptId)
            : Attempt.ClaimAgentWithAssignment(
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
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            bool preferenceStillCurrent;
            bool stopPolicyStillCurrent;
            Error? accountUsageError;
            bool modeStillAdmitted;
            Error? repairSourceError = null;
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

                // A repair's source is re-read here, after that first write statement: it has
                // taken this transaction's database write lock (a matching row is rewritten in
                // place; even zero matches still open the write transaction), so no other claim
                // can commit between this re-check and this claim's own insert. Only a still-current
                // preference proceeds; a stale one is reported as the preference conflict below.
                if (preferenceStillCurrent && stopPolicyStillCurrent && accountUsageError is null && command.RepairSourceAttemptId is { } repairSourceAtCommit)
                {
                    repairSourceError = await PlanningRepairSource.EvaluateAsync(
                        dbContext, run.Id, repairSourceAtCommit, workspace.Id, checkpoint.Id,
                        checkpoint.FingerprintSha256, cancellationToken);
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
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (!modeStillAdmitted)
            {
                // The run no longer admits Agent work. Rolled back before any insert: no attempt, artifact,
                // reservation, or authorization is consumed, and the sealed manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(CurrentRunExecutionMode.NotAdmitted());
            }

            if (!preferenceStillCurrent)
            {
                // A concurrent preference-only change committed in the gap between the read above
                // and this guard — this claim fails safely, cleaning up the already-sealed
                // manifest file, rather than durably embed a pair that is no longer current.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(Error.Conflict(
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
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            if (accountUsageError is not null)
            {
                // The account-usage stop changed, its launch tuple changed, or its evidence is no longer fresh: rolled back before any
                // insert, the sealed manifest removed and nothing consumed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(accountUsageError);
            }

            if (repairSourceError is not null)
            {
                // The source became ineligible (a newer attempt, a competing repair, or any other
                // change) between the request-time check and this boundary: no repair is claimed
                // and the sealed manifest is removed, exactly like a changed preference.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(repairSourceError);
            }

            dbContext.Attempts.Add(attempt);
            dbContext.Artifacts.Add(manifestArtifact);

            if (run.Lifecycle == RunLifecycle.Created)
            {
                run.Claim(nowUtc);
            }

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

                // The exception means this DbContext's change tracker no longer reliably reflects
                // what actually committed — the cause is never inferred from what this request
                // attempted, only from an independent probe of what the database actually holds
                // now.
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    // Committed despite the thrown exception (e.g. the failure came from an
                    // unrelated statement later in the same batch) — nothing to clean up, and
                    // reporting a conflict or deleting this request's own artifact here would both
                    // be false.
                    return Result<CreateCodexPlanningAttemptCommandResult>.Success(
                        new CreateCodexPlanningAttemptCommandResult(attemptId, attemptNumber, command.RepairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // The independent probe could not establish durable state within its own
                    // bound. The sealed file is preserved exactly as a genuinely persisted
                    // Attempt's would be — it may still be referenced — and this is reported as
                    // unresolved rather than asserted as either success or a definite failure.
                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                // Never persisted: the sealed manifest artifact this request already wrote is
                // certain to never be referenced by any Artifact row, so it is cleaned up
                // regardless of what actually caused the failure. The two entities this call added
                // are also removed from tracking so this DbContext instance can never later
                // accidentally re-attempt to persist a known-failed insert if its scope continues.
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                dbContext.Artifacts.Remove(manifestArtifact);

                if (command.RepairSourceAttemptId is { } repairSourceOnRace
                    && await dbContext.Attempts.AsNoTracking().AnyAsync(
                        candidate => candidate.AgentRepairSourceAttemptId == repairSourceOnRace, cancellationToken))
                {
                    // The race the (AgentRepairSourceAttemptId) unique index exists to close: a
                    // concurrent request already committed the one repair of this source.
                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(Error.Conflict(
                        PlanningRepairSource.AlreadyRequestedCode, "A repair was already requested for this attempt."));
                }

                var competingRunningAttemptExists = await dbContext.Attempts
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
                if (competingRunningAttemptExists)
                {
                    // The race the filtered unique index exists to close: a concurrent request
                    // committed its own Running attempt for this run first.
                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                        return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                        if (projectedAgentInvocationTimeOnRace is null)
                        {
                            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                        {
                            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                                Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                        }
                    }

                    // A concurrent claim that also concluded with recorded usage can carry this provider over
                    // its token stop even while count capacity remains; that is never a retryable slot conflict.
                    var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
                    if (tokenStopOnRace is not null)
                    {
                        return Result<CreateCodexPlanningAttemptCommandResult>.Failure(tokenStopOnRace);
                    }

                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
                }

                // Not a race loss on any known invariant — no competing Running attempt and no
                // competing budget-slot claim exist for this run at all. Some other persistence
                // failure caused this (disk, corruption, an unrelated constraint); never report a
                // conflict that would falsely imply a race that never happened.
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                    return Result<CreateCodexPlanningAttemptCommandResult>.Success(
                        new CreateCodexPlanningAttemptCommandResult(attemptId, attemptNumber, command.RepairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // Preserve the sealed file exactly as a genuinely persisted Attempt's would
                    // be; the independent probe could not establish durable state within its own
                    // bound, so this is never asserted as success or failure.
                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                dbContext.Artifacts.Remove(manifestArtifact);
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }
        }

        return Result<CreateCodexPlanningAttemptCommandResult>.Success(
            new CreateCodexPlanningAttemptCommandResult(attempt.Id, attempt.AttemptNumber, command.RepairSourceAttemptId));
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
}
