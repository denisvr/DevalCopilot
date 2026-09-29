using System.Data.Common;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;

/// <summary>
/// Claims one durable Codex challenge-resolution attempt. Mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommandHandler</c>'s workspace/lease/checkpoint
/// eligibility chain and sealed-manifest/persistence-race handling exactly, plus the additional
/// chain of checks that the named Challenged review attempt, its original Proposal, and its
/// complete Challenge set are all real, current, mutually consistent, and not already
/// successfully resolved.
/// </summary>
public sealed class CreateChallengeResolutionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe)
    : ICommandHandler<CreateChallengeResolutionAttemptCommand, Result<CreateChallengeResolutionAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.ChallengeResolution);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateChallengeResolutionAttemptCommandResult>> HandleAsync(
        CreateChallengeResolutionAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start a challenge-resolution attempt."));
        }

        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
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
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
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
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and before any provider-availability probe, Git work, or
        // manifest sealing. Unconfigured, it reads nothing and changes nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
        if (tokenStopError is not null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(tokenStopError);
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a challenge resolution."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a challenge resolution."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a challenge resolution."));
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null || codexSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureWithUntrackedPreviewsAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var challengeValidation = await ValidateChallengedReviewAsync(run.Id, command.ChallengedReviewAttemptId, workspace.Id, checkpoint, cancellationToken);
        if (challengeValidation.Error is { } error)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(error);
        }

        var (originalProposalMessage, orderedChallenges) = (challengeValidation.OriginalProposal!, challengeValidation.OrderedChallenges!);
        var challengeMessageIds = orderedChallenges.Select(challenge => challenge.Id).ToList();

        if (await HasSuccessfulResolutionAsync(challengeMessageIds, cancellationToken))
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(AlreadyResolvedError());
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestJson = ChallengeResolutionContextManifestBuilder.Build(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            originalProposalMessage.Id,
            originalProposalMessage.Summary,
            originalProposalMessage.StructuredContentJson,
            orderedChallenges
                .Select(challenge => new ChallengeResolutionContextManifestBuilder.ChallengeEvidence(
                    challenge.Id, challenge.Summary, challenge.StructuredContentJson))
                .ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff,
            evidence.UntrackedFiles);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields, the fixed maximum
            // Challenge cardinality, and the evidence reader's own capture bound, but never
            // silently truncated or persisted over the bound if a future field addition
            // regresses this.
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        // The final durable claim boundary: a genuinely fresh, untracked read of the Run's own
        // current preference, taken only now — after the external Git evidence capture and
        // artifact-sealing work above have both already completed. See CurrentCodexAssignmentPreference.
        var (requestedModel, requestedEffort) = await CurrentCodexAssignmentPreference.ReadAsync(dbContext, run.Id, cancellationToken);

        var attempt = Attempt.ClaimAgentChallengeResolutionWithAssignment(
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

        // The one authoritative record of this attempt's exact ordered input set: the original
        // Proposal at sequence 0, then every Challenge in timeline order — never a second,
        // competing column on Attempt itself.
        var inputMessages = new List<AttemptInputMessage>(orderedChallenges.Count + 1)
        {
            AttemptInputMessage.Record(Guid.NewGuid(), attemptId, originalProposalMessage.Id, sequence: 0),
        };
        for (var index = 0; index < orderedChallenges.Count; index++)
        {
            inputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, orderedChallenges[index].Id, sequence: index + 1));
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
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            bool preferenceStillCurrent;
            bool stopPolicyStillCurrent;
            Error? lateEligibilityError;
            try
            {
                // One atomic UPDATE ... WHERE statement requiring the Run's requested model/effort
                // to still exactly match what was just read above.
                preferenceStillCurrent = await CurrentCodexAssignmentPreference.ConfirmUnchangedAsync(
                    dbContext, run.Id, requestedModel, requestedEffort, cancellationToken);

                // The token stop policy the claim decided against must be unchanged too: one more
                // atomic UPDATE ... WHERE compare inside this same transaction (see CurrentTokenStopPolicy).
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);

                // The two guard writes above already hold the database write lock, so this final
                // read of the reviewed lineage and of any competing resolution is atomic with the
                // Attempt insert below: a result committed after the earlier checks is seen here.
                lateEligibilityError = await RecheckEligibilityAsync(
                    run.Id, command.ChallengedReviewAttemptId, workspace.Id, checkpoint, challengeMessageIds, cancellationToken);
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
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (!preferenceStillCurrent)
            {
                // A concurrent preference-only change committed in the gap between the read above
                // and this guard — this claim fails safely, cleaning up the already-sealed
                // manifest file, rather than durably embed a pair that is no longer current.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(Error.Conflict(
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
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            if (lateEligibilityError is not null)
            {
                // The reviewed lineage or its resolution state changed after the earlier checks. The
                // transaction is rolled back before any insert and the sealed manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(lateEligibilityError);
            }

            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.AddRange(inputMessages);
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
                    foreach (var inputMessage in inputMessages)
                    {
                        dbContext.AttemptInputMessages.Remove(inputMessage);
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

                // The exception means this DbContext's change tracker no longer reliably reflects
                // what actually committed — the cause is never inferred from what this request
                // attempted, only from an independent probe of what the database actually holds
                // now.
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateChallengeResolutionAttemptCommandResult>.Success(
                        new CreateChallengeResolutionAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // The independent probe could not establish durable state within its own
                    // bound. The sealed file is preserved exactly as a genuinely persisted
                    // Attempt's would be — it may still be referenced — and this is reported as
                    // unresolved rather than asserted as either success or a definite failure.
                    return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                foreach (var inputMessage in inputMessages)
                {
                    dbContext.AttemptInputMessages.Remove(inputMessage);
                }

                dbContext.Artifacts.Remove(manifestArtifact);

                var competingRunningAttemptExists = await dbContext.Attempts
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
                if (competingRunningAttemptExists)
                {
                    return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
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
                        return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
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
                            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                        if (projectedAgentInvocationTimeOnRace is null)
                        {
                            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                        {
                            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                                Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                        }
                    }

                    // A concurrent claim that also concluded with recorded usage can carry this provider over
                    // its token stop even while count capacity remains; that is never a retryable slot conflict.
                    var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.Codex, cancellationToken);
                    if (tokenStopOnRace is not null)
                    {
                        return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(tokenStopOnRace);
                    }

                    return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
                }

                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
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
                    foreach (var inputMessage in inputMessages)
                    {
                        dbContext.AttemptInputMessages.Remove(inputMessage);
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
                    return Result<CreateChallengeResolutionAttemptCommandResult>.Success(
                        new CreateChallengeResolutionAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    // Preserve the sealed file exactly as a genuinely persisted Attempt's would
                    // be; the independent probe could not establish durable state within its own
                    // bound, so this is never asserted as success or failure.
                    return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                dbContext.Attempts.Remove(attempt);
                foreach (var inputMessage in inputMessages)
                {
                    dbContext.AttemptInputMessages.Remove(inputMessage);
                }

                dbContext.Artifacts.Remove(manifestArtifact);
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }
        }

        return Result<CreateChallengeResolutionAttemptCommandResult>.Success(
            new CreateChallengeResolutionAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
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
    /// Validates every fact this slice requires about the named Challenged review attempt before
    /// this resolution attempt is ever claimed: it belongs to this run, is a completed,
    /// successful ClaudeCode critical-review attempt bound to the exact same
    /// workspace/checkpoint this resolution attempt is about to claim; its original Proposal is a
    /// real, provider-observed Codex Proposal, itself bound to that same workspace/checkpoint;
    /// and its complete Challenge set is a non-empty, provider-observed Claude set that all
    /// replies to that exact Proposal.
    /// </summary>
    private async Task<ChallengedReviewValidation> ValidateChallengedReviewAsync(
        Guid runId, Guid challengedReviewAttemptId, Guid workspaceId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateChallengedReviewCoreAsync(runId, challengedReviewAttemptId, workspaceId, checkpoint, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A persisted row of this review, its lineage, or the run could not be materialized
            // (typically an unparseable stored enum string). Fails closed with a fixed refusal that
            // echoes neither the exception nor the stored value. The guard catches InvalidOperationException,
            // which cannot prove an enum-conversion cause and could have another origin; DbException and
            // cancellation exceptions are not caught by it.
            return ChallengedReviewValidation.Failed(PlanningLineage.UnreadableEvidenceError());
        }
    }

    private async Task<ChallengedReviewValidation> ValidateChallengedReviewCoreAsync(
        Guid runId, Guid challengedReviewAttemptId, Guid workspaceId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var challengedReviewAttempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == challengedReviewAttemptId, cancellationToken);
        if (challengedReviewAttempt is null || challengedReviewAttempt.RunId != runId)
        {
            return ChallengedReviewValidation.Failed(
                Error.NotFound("agent_attempts.challenged_review_not_found", "The requested challenged review attempt was not found for this run."));
        }

        if (challengedReviewAttempt.Kind != AttemptKind.Agent
            || challengedReviewAttempt.AgentRole != AgentRole.CriticalReviewer
            || challengedReviewAttempt.AgentResponseContract != AgentResponseContract.CriticalReview
            || challengedReviewAttempt.AgentProvider is not { } challengedReviewProvider
            || !Enum.IsDefined(challengedReviewProvider)
            || !AgentAttemptIdentity.IsCoherent(challengedReviewAttempt)
            || challengedReviewAttempt.Status != AttemptStatus.Completed
            || challengedReviewAttempt.AgentOutcome != AgentOutcome.Challenged)
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenged_review_not_valid",
                    "The requested attempt did not complete as a successful, challenged Claude critical review."));
        }

        if (challengedReviewAttempt.AgentGitWorkspaceId != workspaceId
            || challengedReviewAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(challengedReviewAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenged_review_checkpoint_stale",
                    "The challenged review was made against a different checkpoint than the one currently current for this workspace."));
        }

        var originalProposalMessageId = await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == challengedReviewAttempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => (Guid?)inputMessage.CollaborationMessageId)
            .SingleOrDefaultAsync(cancellationToken);
        if (originalProposalMessageId is not { } reviewedProposalId)
        {
            return ChallengedReviewValidation.Failed(PlanningLineage.ToClaimError(PlanningLineage.FailureKind.LineageInvalid));
        }

        // The reviewed Proposal is either a Planner root or a Resolver's first revision; both are
        // decided from the same durable lineage rule the review claim uses. A depth-two revision was
        // never reviewable, so a review of one cannot exist as valid evidence here.
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, runId, cancellationToken);
        if (snapshot is null)
        {
            return ChallengedReviewValidation.Failed(PlanningLineage.UnreadableEvidenceError());
        }

        var lineage = PlanningLineage.Evaluate(
            snapshot, runId, workspaceId, checkpoint.Id, checkpoint.FingerprintSha256, reviewedProposalId);
        if (lineage.Node is not { } lineageNode)
        {
            return ChallengedReviewValidation.Failed(
                PlanningLineage.ToClaimError(lineage.Failure ?? PlanningLineage.FailureKind.LineageInvalid));
        }

        if (lineageNode.Depth > PlanningLineage.MaximumReviewableDepth)
        {
            return ChallengedReviewValidation.Failed(PlanningLineage.ExhaustedError());
        }

        var originalProposalMessage = lineageNode.Proposal;

        // Every Challenge already belongs to challengedReviewAttempt by construction of the query
        // below, whose own role (CriticalReviewer) and provider were already validated (present
        // and defined) above — provenance integrity here means each Challenge's own recorded Actor
        // must still truthfully match that SAME already-validated attempt's provider, never a
        // fixed literal.
        var expectedChallengeActor = ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, challengedReviewProvider);
        var orderedChallenges = await dbContext.CollaborationMessages
            .Where(candidate => candidate.AttemptId == challengedReviewAttempt.Id && candidate.Type == CollaborationMessageType.Challenge)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        if (orderedChallenges.Count == 0
            || orderedChallenges.Any(challenge =>
                challenge.Provenance != CollaborationMessageProvenance.ProviderObserved
                || challenge.Actor != expectedChallengeActor
                || challenge.InReplyToMessageId != originalProposalMessage.Id))
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenges_not_valid",
                    "The challenged review's challenge set is not a valid, complete, provider-observed set replying to its original proposal."));
        }

        return ChallengedReviewValidation.Succeeded(originalProposalMessage, orderedChallenges);
    }

    private async Task<bool> HasSuccessfulResolutionAsync(IReadOnlyList<Guid> challengeMessageIds, CancellationToken cancellationToken) =>
        await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate =>
                candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Resolver
                && candidate.AgentOutcome == AgentOutcome.Resolved)
            .Join(
                dbContext.AttemptInputMessages.AsNoTracking().Where(inputMessage => challengeMessageIds.Contains(inputMessage.CollaborationMessageId)),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate)
            .AnyAsync(cancellationToken);

    private static Error AlreadyResolvedError() =>
        Error.Conflict("agent_attempts.already_resolved", "This challenged review already has a successful resolution.");

    /// <summary>The final, in-transaction read: the same reviewed-lineage validation and
    /// competing-resolution check the claim already passed, decided again against the state the
    /// write lock now protects. Returns the refusal, or <see langword="null"/> when still eligible.</summary>
    private async Task<Error?> RecheckEligibilityAsync(
        Guid runId,
        Guid challengedReviewAttemptId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        IReadOnlyList<Guid> challengeMessageIds,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateChallengedReviewAsync(runId, challengedReviewAttemptId, workspaceId, checkpoint, cancellationToken);
        if (validation.Error is { } validationError)
        {
            return validationError;
        }

        var currentChallengeIds = validation.OrderedChallenges!.Select(challenge => challenge.Id).ToList();
        if (!currentChallengeIds.SequenceEqual(challengeMessageIds))
        {
            return PlanningLineage.ToClaimError(PlanningLineage.FailureKind.LineageInvalid);
        }

        return await HasSuccessfulResolutionAsync(challengeMessageIds, cancellationToken) ? AlreadyResolvedError() : null;
    }

    private sealed record ChallengedReviewValidation(
        CollaborationMessage? OriginalProposal, IReadOnlyList<CollaborationMessage>? OrderedChallenges, Error? Error)
    {
        public static ChallengedReviewValidation Succeeded(CollaborationMessage originalProposal, IReadOnlyList<CollaborationMessage> orderedChallenges) =>
            new(originalProposal, orderedChallenges, null);

        public static ChallengedReviewValidation Failed(Error error) => new(null, null, error);
    }
}
