using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

/// <summary>
/// See <see cref="RecordImplementationResultCommand"/> for why this handler — unlike its
/// challenge-resolution counterpart — is the one place that classifies the terminal
/// <see cref="AgentOutcome"/> from raw evidence, rather than receiving it pre-decided. Every
/// boundary value this command carries (the starting checkpoint reference, the completion
/// HEAD/fingerprint shapes, every observed changed path, the sealed artifacts, and the report
/// itself) is independently re-validated here before any mutation — this handler never assumes
/// the supervisor, the adapter, or <see cref="ImplementationResponseParser"/> already did so
/// correctly, and every malformed case fails closed to a safe <see cref="Result"/> failure,
/// never an unhandled exception.
/// </summary>
public sealed class RecordImplementationResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordImplementationResultCommand, Result<RecordImplementationResultCommandResult>>
{
    /// <summary>The one fixed, safe reason code recorded whenever this handler cannot rule out
    /// that the worktree was mutated by an attempt that did not end in a verified, trustworthy
    /// Implemented outcome. Never anything more specific: the exact nature of the ambiguity is
    /// never guessed from the failure path that produced it.</summary>
    private const string ImplementationOutcomeAmbiguousReasonCode = "workspaces.implementation_outcome_ambiguous";

    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordImplementationResultCommandResult>> HandleAsync(
        RecordImplementationResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentProvider != AgentProvider.ClaudeCode
            || attempt.AgentRole != AgentRole.Implementer
            || attempt.AgentResponseContract != AgentResponseContract.ImplementationReport)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Conflict("attempts.not_implementation", "The attempt is not a Claude implementation attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.not_dispatched",
                    "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        var workspace = await dbContext.GitWorkspaces.SingleOrDefaultAsync(
            candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
        if (workspace is null)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.NotFound("workspaces.not_found", "The attempt's owning workspace was not found."));
        }

        // The starting checkpoint is the immutable evidence every classification below compares
        // against (most importantly, its own HeadCommitSha) — never assumed to exist, to still
        // belong to this attempt's workspace, or to still agree with the attempt's own recorded
        // starting fingerprint merely because the attempt records its id. A mismatch on any of
        // these is corrupted or inconsistent command state, never a provider outcome.
        var startingCheckpoint = await dbContext.GitCheckpoints.SingleOrDefaultAsync(
            candidate => candidate.Id == attempt.AgentGitCheckpointId, cancellationToken);
        if (startingCheckpoint is null
            || startingCheckpoint.WorkspaceId != workspace.Id
            || !string.Equals(startingCheckpoint.FingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.starting_checkpoint_invalid",
                    "The attempt's starting checkpoint no longer exists, no longer belongs to its workspace, or no longer agrees with the attempt's own recorded starting fingerprint."));
        }

        // A present-but-malformed value is never treated the same as legitimately unavailable
        // evidence (null, because capture itself failed) — it signals a caller/evidence-reader
        // defect this handler must never paper over by guessing which shape was intended.
        if (command.CompletionHeadCommitSha is { } headCommitSha && !ImplementationEvidenceValidation.IsValidCommitSha(headCommitSha))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_completion_head", "The reported completion HEAD commit SHA is not a valid shape."));
        }

        if (command.CompletionFingerprintSha256 is { } fingerprint && !ImplementationEvidenceValidation.IsValidFingerprintSha256(fingerprint))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.invalid_completion_fingerprint", "The reported completion fingerprint is not a valid shape."));
        }

        if (!ImplementationEvidenceValidation.AreValidObservedChangedPaths(
                command.ObservedChangedPaths, ImplementationReportOutputSchema.MaximumChangedPaths))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.invalid_observed_changed_paths",
                    "One of the independently observed changed paths is unsafe, unbounded, duplicated, or has an invalid Git status value."));
        }

        // Completion-evidence coherence: HEAD, fingerprint, and the observed changed-path list
        // must together describe exactly one of two legitimate situations — evidence was
        // completely unavailable, or it was fully captured — never a partial mix, and never a
        // changed/unchanged fingerprint paired with a changed-path count that contradicts it.
        // Any other combination is invalid caller/evidence-reader data, never a provider outcome.
        var headPresent = command.CompletionHeadCommitSha is not null;
        var fingerprintPresent = command.CompletionFingerprintSha256 is not null;
        if (headPresent != fingerprintPresent || (!headPresent && command.ObservedChangedPaths.Count > 0))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.incoherent_completion_evidence",
                    "The reported completion HEAD, fingerprint, and observed changed-path evidence are not a coherent combination."));
        }

        if (headPresent && string.Equals(command.CompletionHeadCommitSha, startingCheckpoint.HeadCommitSha, StringComparison.Ordinal))
        {
            var fingerprintChangedForCoherence = !string.Equals(
                command.CompletionFingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal);
            if (fingerprintChangedForCoherence != command.ObservedChangedPaths.Count > 0)
            {
                return Result<RecordImplementationResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.incoherent_completion_evidence",
                        "The reported completion HEAD, fingerprint, and observed changed-path evidence are not a coherent combination."));
            }
        }

        // Never relies solely on ImplementationResponseParser having already validated this
        // value — a caller could hand this command a hand-built ValidatedImplementationReport.
        // A supplied-but-invalid report is a malformed boundary object, not a truthful provider
        // outcome: it fails this command closed, with zero mutation, exactly like every other
        // boundary violation above — never silently downgraded to InvalidStructuredOutput, which
        // is reserved for a real provider response that genuinely failed parsing.
        if (command.Report is not null && !ImplementationReportValidation.IsValid(command.Report))
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.invalid_implementation_report",
                    "The supplied implementation report failed independent re-validation."));
        }

        if (!command.ProcessSucceeded && command.Report is not null)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.conflicting_implementation_evidence",
                    "A validated implementation report was supplied for a failed provider invocation."));
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Result<RecordImplementationResultCommandResult>.Failure(
                    Error.Conflict(
                        "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Result<RecordImplementationResultCommandResult>.Failure(
                    Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Result<RecordImplementationResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.invalid_artifact_metadata", "One of the reported artifacts has malformed metadata."));
            }
        }

        // command.Report is now guaranteed either null or already independently re-validated —
        // the boundary check above never lets an invalid-but-non-null report reach this point.
        var evidenceAvailable = command.CompletionHeadCommitSha is not null;
        var (outcome, mutationSuspected) = Classify(command, attempt, startingCheckpoint, evidenceAvailable, command.Report);

        var nowUtc = timeProvider.GetUtcNow();

        try
        {
            attempt.RecordAgentObservedAssignment(command.ObservedModel, command.ObservedEffort);
        }
        catch (ArgumentException)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_assignment_observation", "The provider assignment observation is malformed."));
        }
        catch (InvalidOperationException)
        {
            return Result<RecordImplementationResultCommandResult>.Failure(
                Error.Conflict("agent_attempts.assignment_observation_already_recorded", "The provider assignment observation was already recorded."));
        }

        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        Guid? resultCheckpointId = null;
        if (outcome == AgentOutcome.Implemented)
        {
            var checkpointId = Guid.NewGuid();
            var changedFiles = command.ObservedChangedPaths
                .Select(path => GitChangedFile.Observe(
                    Guid.NewGuid(), checkpointId, path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus))
                .ToArray();
            var checkpoint = GitCheckpoint.Capture(
                checkpointId,
                workspace.Id,
                workspace.ReserveCheckpointNumber(),
                nowUtc,
                command.CompletionHeadCommitSha!,
                command.CompletionFingerprintSha256!,
                changedFiles);

            dbContext.GitCheckpoints.Add(checkpoint);
            dbContext.GitChangedFiles.AddRange(changedFiles);
            resultCheckpointId = checkpoint.Id;
        }

        attempt.CompleteImplementation(outcome, resultCheckpointId, nowUtc);

        if (mutationSuspected)
        {
            workspace.MarkNeedsAttention(ImplementationOutcomeAmbiguousReasonCode);
        }

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, nowUtc);
        }

        RunEvent latestEvent;
        if (outcome == AgentOutcome.Implemented)
        {
            var planProposalMessageId = await ImplementationInputIdentity.GetPlanProposalMessageIdAsync(dbContext, attempt.Id, cancellationToken);
            latestEvent = RecordExecutionReport(attempt, planProposalMessageId, command.Report!, nowUtc);
        }
        else
        {
            latestEvent = RunEvent.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                RunEventType.AgentAttemptCompleted,
                ParticipantIdentity.ForOrchestrator(),
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = outcome.ToString() }),
                nowUtc);
            dbContext.Events.Add(latestEvent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordImplementationResultCommandResult>.Success(
            new RecordImplementationResultCommandResult(attempt.Status, outcome, latestEvent.Sequence));
    }

    /// <summary>
    /// The one place this slice decides what actually happened, per the project's
    /// implementation-truthfulness rules: never guess success, never roll back a mutation, and
    /// flag the workspace whenever the worktree may have changed without a verified, trustworthy
    /// result to show for it. <paramref name="validatedReport"/> is only ever null or an already
    /// independently re-validated report — the boundary check in <see cref="HandleAsync"/> above
    /// already turned any supplied-but-invalid report into a command failure before this method
    /// is ever called, so a non-null value here is always trustworthy.
    /// </summary>
    private static (AgentOutcome Outcome, bool MutationSuspected) Classify(
        RecordImplementationResultCommand command,
        Attempt attempt,
        GitCheckpoint startingCheckpoint,
        bool evidenceAvailable,
        ValidatedImplementationReport? validatedReport)
    {
        if (!evidenceAvailable)
        {
            // The process may have run for some time before this failure, and fresh evidence
            // could not be captured at all — never assumed safe.
            return (AgentOutcome.CheckpointEvidenceUnavailable, true);
        }

        var fingerprintChanged = !string.Equals(
            command.CompletionFingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal);

        // Claude's implementation tool allowlist never includes Git or any process tool, so it
        // can never move HEAD itself. A changed HEAD is checked before anything else the report
        // might claim: it is proof of an external or unauthorized mutation, never a trustworthy
        // implementation, regardless of whether the process succeeded or the report's own
        // changed-path set would otherwise appear to match.
        var headChanged = !string.Equals(
            command.CompletionHeadCommitSha, startingCheckpoint.HeadCommitSha, StringComparison.Ordinal);

        if (!command.ProcessSucceeded)
        {
            return (AgentOutcome.ProviderInvocationFailed, fingerprintChanged || headChanged);
        }

        if (headChanged)
        {
            return (AgentOutcome.ImplementationHeadChanged, true);
        }

        if (validatedReport is null)
        {
            return (AgentOutcome.InvalidStructuredOutput, fingerprintChanged);
        }

        if (!fingerprintChanged)
        {
            return (AgentOutcome.NoChangesProduced, false);
        }

        var reportedPaths = validatedReport.ChangedRelativePaths;
        var reportedPathSet = new HashSet<string>(reportedPaths, StringComparer.Ordinal);
        var observedPathSet = new HashSet<string>(command.ObservedChangedPaths.Select(path => path.Path), StringComparer.Ordinal);

        // Duplicates in the report already collapse the set below its own list count — treated
        // the same as any other mismatch, never silently tolerated. (ImplementationReportValidation
        // already rejects a duplicated path, so this is defense in depth, not the only check.)
        var reportMatchesObservedEvidenceExactly = reportedPathSet.Count == reportedPaths.Count && reportedPathSet.SetEquals(observedPathSet);

        // The worktree changed, but Claude's own account of what it changed does not exactly
        // match independently observed Git evidence — never trusted enough to record a successful checkpoint,
        // and always flagged for a human to inspect the actual diff.
        return reportMatchesObservedEvidenceExactly
            ? (AgentOutcome.Implemented, false)
            : (AgentOutcome.InvalidStructuredOutput, true);
    }

    private RunEvent RecordExecutionReport(
        Attempt attempt, Guid planProposalMessageId, ValidatedImplementationReport report, DateTimeOffset nowUtc)
    {
        var structuredContentJson = JsonSerializer.Serialize(new
        {
            completedWork = report.ImplementationNotes,
            verification = report.RecommendedVerification,
        });

        var message = CollaborationMessage.RecordAgent(
            attempt,
            Guid.NewGuid(),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.ExecutionReport,
            planProposalMessageId,
            report.Summary,
            structuredContentJson,
            nowUtc);
        dbContext.CollaborationMessages.Add(message);

        var runEvent = RunEvent.Record(
            Guid.NewGuid(),
            attempt.RunId,
            attempt.Id,
            RunEventType.CollaborationMessageRecorded,
            message.Actor,
            JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                type = message.Type.ToString(),
                provenance = message.Provenance.ToString(),
            }),
            nowUtc);
        dbContext.Events.Add(runEvent);

        return runEvent;
    }

    private void RecordArtifact(Guid runId, Guid attemptId, SealedImplementationArtifact sealedArtifact, DateTimeOffset nowUtc)
    {
        var mediaType = sealedArtifact.Purpose == ArtifactPurpose.AgentFinalResponse
            ? "application/json"
            : "text/plain; charset=utf-8";

        var artifact = Artifact.Record(
            Guid.NewGuid(),
            runId,
            attemptId,
            sealedArtifact.Purpose,
            mediaType,
            sealedArtifact.RelativeStoragePath,
            sealedArtifact.ContentHash,
            sealedArtifact.ByteLength,
            sealedArtifact.Truncated,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(artifact);
    }
}
