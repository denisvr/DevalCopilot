using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;

/// <summary>Independently validates correction output and Git evidence before atomically recording
/// the terminal attempt, result checkpoint, ordered revision responses, execution report, artifacts,
/// and events.</summary>
public sealed class RecordReviewCorrectionResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordReviewCorrectionResultCommand, Result<RecordReviewCorrectionResultCommandResult>>
{
    private const string AmbiguousMutationReasonCode = "workspaces.correction_outcome_ambiguous";
    private const int MaxProviderSessionIdLength = 256;

    private static readonly IReadOnlySet<ArtifactPurpose> SupportedPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    public async Task<Result<RecordReviewCorrectionResultCommandResult>> HandleAsync(
        RecordReviewCorrectionResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.Implementer
            || attempt.AgentResponseContract != AgentResponseContract.ReviewCorrection)
        {
            return Failure(Error.Conflict("attempts.not_review_correction", "The attempt is not a review-correction attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || !attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Failure(Error.Conflict("attempts.not_active", "The attempt is not an active dispatched correction."));
        }

        if (!Enum.IsDefined(attempt.AgentOutcome.GetValueOrDefault()) && attempt.AgentOutcome.HasValue)
        {
            return Failure(Error.Failure("agent_attempts.invalid_outcome", "The attempt contains an invalid outcome."));
        }

        var workspace = await dbContext.GitWorkspaces.SingleOrDefaultAsync(
            candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
        var startingCheckpoint = await dbContext.GitCheckpoints.SingleOrDefaultAsync(
            candidate => candidate.Id == attempt.AgentGitCheckpointId, cancellationToken);
        if (workspace is null || startingCheckpoint is null
            || startingCheckpoint.WorkspaceId != workspace.Id
            || !string.Equals(startingCheckpoint.FingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.starting_checkpoint_invalid", "The correction starting checkpoint is invalid."));
        }

        if (workspace.Status != WorkspaceStatus.Ready
            || !await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.workspace_not_ready", "The correction workspace is no longer eligible."));
        }

        var currentCheckpointId = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentCheckpointId != startingCheckpoint.Id)
        {
            return Failure(Error.Conflict("agent_attempts.starting_checkpoint_not_current", "The correction starting checkpoint is no longer current."));
        }

        if (command.CompletionHeadCommitSha is { } head && !ImplementationEvidenceValidation.IsValidCommitSha(head))
        {
            return Failure(Error.Failure("agent_attempts.invalid_completion_head", "The completion HEAD has an invalid shape."));
        }

        if (command.CompletionFingerprintSha256 is { } fingerprint
            && !ImplementationEvidenceValidation.IsValidFingerprintSha256(fingerprint))
        {
            return Failure(Error.Failure("agent_attempts.invalid_completion_fingerprint", "The completion fingerprint has an invalid shape."));
        }

        if (!ImplementationEvidenceValidation.AreValidObservedChangedPaths(
                command.ObservedChangedPaths, ImplementationReportOutputSchema.MaximumChangedPaths))
        {
            return Failure(Error.Failure("agent_attempts.invalid_observed_changed_paths", "Observed changed paths are invalid."));
        }

        var headPresent = command.CompletionHeadCommitSha is not null;
        var fingerprintPresent = command.CompletionFingerprintSha256 is not null;
        if (headPresent != fingerprintPresent || (!headPresent && command.ObservedChangedPaths.Count > 0))
        {
            return Failure(Error.Failure("agent_attempts.incoherent_completion_evidence", "Completion Git evidence is incomplete or contradictory."));
        }

        if (headPresent
            && string.Equals(command.CompletionHeadCommitSha, startingCheckpoint.HeadCommitSha, StringComparison.Ordinal))
        {
            var fingerprintChangedForCoherence = !string.Equals(
                command.CompletionFingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal);
            if (fingerprintChangedForCoherence != (command.ObservedChangedPaths.Count > 0))
            {
                return Failure(Error.Failure("agent_attempts.incoherent_completion_evidence", "Completion Git evidence is incomplete or contradictory."));
            }
        }

        if (command.Correction is not null && !IsValidCorrection(command.Correction))
        {
            return Failure(Error.Failure("agent_attempts.invalid_correction_result", "The supplied correction result failed independent validation."));
        }

        if (!command.ProcessSucceeded && command.Correction is not null)
        {
            return Failure(Error.Failure("agent_attempts.conflicting_correction_evidence", "A correction result was supplied for a failed invocation."));
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Failure(Error.Failure("agent_attempts.provider_session_id_too_long", "The provider session identifier exceeds its bound."));
        }

        var artifactValidation = ValidateArtifacts(command.SealedArtifacts);
        if (artifactValidation is not null)
        {
            return Failure(artifactValidation);
        }

        var orderedInputs = await ReviewCorrectionInputIdentity.GetOrderedInputMessageIdsAsync(
            dbContext, attempt.Id, cancellationToken);
        if (orderedInputs.Count < 2)
        {
            return Failure(Error.Conflict("agent_attempts.correction_input_invalid", "The correction input identity is incomplete."));
        }

        var inputMessages = await dbContext.CollaborationMessages
            .Where(message => orderedInputs.Contains(message.Id) && message.RunId == run.Id)
            .ToDictionaryAsync(message => message.Id, cancellationToken);
        if (inputMessages.Count != orderedInputs.Count
            || orderedInputs.Any(id => !inputMessages.ContainsKey(id))
            || inputMessages[orderedInputs[0]].Type != CollaborationMessageType.ExecutionReport
            || orderedInputs.Skip(1).Any(id => inputMessages[id].Type != CollaborationMessageType.ReviewFinding))
        {
            return Failure(Error.Conflict("agent_attempts.correction_input_invalid", "The correction input identity is not valid."));
        }

        var previousReportValidation = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext,
            inputMessages[orderedInputs[0]],
            run.Id,
            workspace.Id,
            startingCheckpoint.Id,
            cancellationToken);
        if (previousReportValidation is null)
        {
            return Failure(Error.Conflict("agent_attempts.correction_input_invalid", "The correction execution report is not a valid implementation result."));
        }

        var orderedFindingMessages = orderedInputs.Skip(1).Select(id => inputMessages[id]).ToArray();
        foreach (var finding in orderedFindingMessages)
        {
            var findingOwner = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
                dbContext, finding, run.Id, AgentRole.CodeReviewer, cancellationToken);
            if (finding.Provenance != CollaborationMessageProvenance.ProviderObserved
                || finding.InReplyToMessageId != orderedInputs[0]
                || findingOwner is null
                || findingOwner.AgentResponseContract != AgentResponseContract.ImplementationReview
                || findingOwner.Status != AttemptStatus.Completed
                || findingOwner.AgentOutcome != AgentOutcome.ReviewChangesRequested
                || findingOwner.AgentGitWorkspaceId != workspace.Id
                || findingOwner.AgentGitCheckpointId != startingCheckpoint.Id)
            {
                return Failure(Error.Conflict("agent_attempts.correction_input_invalid", "The correction findings are not valid review evidence."));
            }
        }

        if (command.Correction is not null
            && !command.Correction.RevisionResponses.Select(response => response.FindingMessageId)
                .SequenceEqual(orderedInputs.Skip(1)))
        {
            return Failure(Error.Failure("agent_attempts.invalid_correction_result", "Correction responses do not match the exact ordered finding identity."));
        }

        var previousExecutionReport = inputMessages[orderedInputs[0]];
        var originalProposal = previousReportValidation.OriginalProposal;

        var (outcome, mutationSuspected) = Classify(command, attempt, startingCheckpoint, inputMessages.Count == orderedInputs.Count);

        var nowUtc = timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        Guid? resultCheckpointId = null;
        if (outcome == AgentOutcome.CorrectionApplied)
        {
            var checkpointId = Guid.NewGuid();
            var changedFiles = command.ObservedChangedPaths.Select(path => GitChangedFile.Observe(
                Guid.NewGuid(), checkpointId, path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus)).ToArray();
            var resultCheckpoint = GitCheckpoint.Capture(
                checkpointId,
                workspace.Id,
                workspace.ReserveCheckpointNumber(),
                nowUtc,
                command.CompletionHeadCommitSha!,
                command.CompletionFingerprintSha256!,
                changedFiles);
            dbContext.GitCheckpoints.Add(resultCheckpoint);
            dbContext.GitChangedFiles.AddRange(changedFiles);
            resultCheckpointId = resultCheckpoint.Id;
        }

        attempt.CompleteReviewCorrection(outcome, resultCheckpointId, nowUtc);
        if (mutationSuspected)
        {
            workspace.MarkNeedsAttention(AmbiguousMutationReasonCode);
        }

        foreach (var artifact in command.SealedArtifacts)
        {
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), run.Id, attempt.Id, artifact.Purpose,
                artifact.Purpose == ArtifactPurpose.AgentFinalResponse ? "application/json" : "text/plain; charset=utf-8",
                artifact.RelativeStoragePath, artifact.ContentHash, artifact.ByteLength, artifact.Truncated,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort,
                ArtifactRetentionPolicy.RetainUntilRunDeleted, nowUtc));
        }

        RunEvent latestEvent;
        if (outcome == AgentOutcome.CorrectionApplied)
        {
            var correction = command.Correction!;
            for (var index = 0; index < correction.RevisionResponses.Count; index++)
            {
                var findingId = orderedInputs[index + 1];
                var response = correction.RevisionResponses[index];
                var finding = inputMessages[findingId];
                var message = CollaborationMessage.RecordAgent(
                    attempt,
                    Guid.NewGuid(),
                    finding.Actor,
                    CollaborationMessageType.RevisionResponse,
                    findingId,
                    response.Disposition,
                    JsonSerializer.Serialize(new
                    {
                        disposition = response.Disposition,
                        evidence = response.Evidence,
                        resultingSourceChanges = response.ResultingSourceChanges,
                    }),
                    nowUtc);
                dbContext.CollaborationMessages.Add(message);
                latestEvent = RecordMessageEvent(message, nowUtc);
            }

            var executionReport = correction.ExecutionReport;
            var reportMessage = CollaborationMessage.RecordAgent(
                attempt,
                Guid.NewGuid(),
                originalProposal.Actor,
                CollaborationMessageType.ExecutionReport,
                originalProposal.Id,
                executionReport.Summary,
                JsonSerializer.Serialize(new
                {
                    completedWork = executionReport.ImplementationNotes,
                    verification = executionReport.RecommendedVerification,
                }),
                nowUtc);
            dbContext.CollaborationMessages.Add(reportMessage);
            latestEvent = RecordMessageEvent(reportMessage, nowUtc);
        }
        else
        {
            latestEvent = RunEvent.Record(
                Guid.NewGuid(), run.Id, attempt.Id, RunEventType.AgentAttemptCompleted,
                ParticipantIdentity.ForOrchestrator(),
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = outcome.ToString() }), nowUtc);
            dbContext.Events.Add(latestEvent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecordReviewCorrectionResultCommandResult>.Success(
            new RecordReviewCorrectionResultCommandResult(attempt.Status, outcome, latestEvent.Sequence));
    }

    private static (AgentOutcome Outcome, bool MutationSuspected) Classify(
        RecordReviewCorrectionResultCommand command,
        Attempt attempt,
        GitCheckpoint startingCheckpoint,
        bool inputIsValid)
    {
        if (!inputIsValid || command.CompletionHeadCommitSha is null || command.CompletionFingerprintSha256 is null)
        {
            return (AgentOutcome.CheckpointEvidenceUnavailable, true);
        }

        var fingerprintChanged = !string.Equals(
            command.CompletionFingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal);
        var headChanged = !string.Equals(
            command.CompletionHeadCommitSha, startingCheckpoint.HeadCommitSha, StringComparison.Ordinal);

        if (!command.ProcessSucceeded)
        {
            return (AgentOutcome.ProviderInvocationFailed, fingerprintChanged || headChanged);
        }

        if (headChanged)
        {
            return (AgentOutcome.CorrectionHeadChanged, true);
        }

        if (command.Correction is null)
        {
            return (AgentOutcome.InvalidStructuredOutput, fingerprintChanged);
        }

        if (string.Equals(command.CompletionFingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return (AgentOutcome.CorrectionNoChangesProduced, false);
        }

        var reportPaths = command.Correction.ExecutionReport.ChangedRelativePaths;
        var observedPaths = command.ObservedChangedPaths.Select(path => path.Path).ToHashSet(StringComparer.Ordinal);
        var reportMatchesObservedEvidenceExactly =
            reportPaths.Count == observedPaths.Count
            && reportPaths.ToHashSet(StringComparer.Ordinal).SetEquals(observedPaths);
        return reportMatchesObservedEvidenceExactly
            ? (AgentOutcome.CorrectionApplied, false)
            : (AgentOutcome.InvalidStructuredOutput, true);
    }

    private static bool IsValidCorrection(ValidatedReviewCorrection correction)
    {
        if (correction.RevisionResponses.Count is < 1 or > ReviewCorrectionOutputSchema.MaximumFindings
            || correction.RevisionResponses.Select(response => response.FindingMessageId).Distinct().Count() != correction.RevisionResponses.Count
            || !correction.RevisionResponses.All(response =>
                CollaborationMessageContentPolicy.IsSafeSummary(response.Disposition)
                && !string.IsNullOrWhiteSpace(response.Evidence)
                && !string.IsNullOrWhiteSpace(response.ResultingSourceChanges)))
        {
            return false;
        }

        foreach (var response in correction.RevisionResponses)
        {
            var content = JsonSerializer.Serialize(new
            {
                disposition = response.Disposition,
                evidence = response.Evidence,
                resultingSourceChanges = response.ResultingSourceChanges,
            });
            try
            {
                CollaborationMessageContentPolicy.Validate(CollaborationMessageType.RevisionResponse, content);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return ImplementationReportValidation.IsValid(correction.ExecutionReport);
    }

    private static Error? ValidateArtifacts(IReadOnlyList<SealedReviewCorrectionArtifact> artifacts)
    {
        var purposes = new HashSet<ArtifactPurpose>();
        foreach (var artifact in artifacts)
        {
            if (!SupportedPurposes.Contains(artifact.Purpose))
            {
                return Error.Conflict("agent_attempts.unsupported_artifact_purpose", "The artifact purpose is not supported.");
            }

            if (!purposes.Add(artifact.Purpose))
            {
                return Error.Conflict("agent_attempts.duplicate_artifact_purpose", "An artifact purpose was reported more than once.");
            }

            if (string.IsNullOrWhiteSpace(artifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(artifact.ContentHash)
                || artifact.ByteLength < 0)
            {
                return Error.Failure("agent_attempts.invalid_artifact_metadata", "Artifact metadata is invalid.");
            }
        }

        return null;
    }

    private RunEvent RecordMessageEvent(CollaborationMessage message, DateTimeOffset nowUtc)
    {
        var runEvent = RunEvent.Record(
            Guid.NewGuid(), message.RunId, message.AttemptId, RunEventType.CollaborationMessageRecorded,
            message.Actor,
            JsonSerializer.Serialize(new { messageId = message.Id, type = message.Type.ToString(), provenance = message.Provenance.ToString() }),
            nowUtc);
        dbContext.Events.Add(runEvent);
        return runEvent;
    }

    private static Result<RecordReviewCorrectionResultCommandResult> Failure(Error error) =>
        Result<RecordReviewCorrectionResultCommandResult>.Failure(error);
}
