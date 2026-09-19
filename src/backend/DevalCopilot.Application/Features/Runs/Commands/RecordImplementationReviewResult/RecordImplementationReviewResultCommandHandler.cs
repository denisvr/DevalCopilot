using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

public sealed class RecordImplementationReviewResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordImplementationReviewResultCommand, Result<RecordImplementationReviewResultCommandResult>>
{
    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    /// <summary>
    /// The explicit, closed policy for what a Codex code-review attempt may report directly
    /// through this command. <see cref="AgentOutcome.SourceChanged"/> and
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> are derived exclusively by their own
    /// dedicated pre-dispatch commands; <see cref="AgentOutcome.InputAlreadyCodeReviewed"/> is
    /// derived exclusively by <c>RecordCodeReviewInputAlreadyCodeReviewedCommand</c>, which
    /// independently re-verifies the competing review before ever recording it. Mirrors
    /// <c>RecordChallengeResolutionResultCommandHandler.CallerSelectableOutcomes</c> exactly.
    /// </summary>
    private static readonly IReadOnlySet<AgentOutcome> CallerSelectableOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.ReviewApproved,
        AgentOutcome.ReviewChangesRequested,
        AgentOutcome.InvalidStructuredOutput,
        AgentOutcome.ProviderInvocationFailed,
        AgentOutcome.CheckpointEvidenceUnavailable,
    };

    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordImplementationReviewResultCommandResult>> HandleAsync(
        RecordImplementationReviewResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentResponseContract != AgentResponseContract.ImplementationReview)
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Conflict("attempts.not_code_review", "The attempt is not a Codex code-review attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.not_dispatched",
                    "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_outcome", "The reported outcome is not a defined agent outcome."));
        }

        if (!CallerSelectableOutcomes.Contains(command.Outcome))
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.outcome_not_caller_selectable",
                    $"{command.Outcome} belongs to a dedicated host-derived recording path and cannot be reported directly here."));
        }

        var executionReportMessageId = await CodeReviewInputIdentity.GetExecutionReportMessageIdAsync(dbContext, attempt.Id, cancellationToken);

        var isReviewOutcome = command.Outcome is AgentOutcome.ReviewApproved or AgentOutcome.ReviewChangesRequested;
        if (isReviewOutcome)
        {
            if (string.IsNullOrWhiteSpace(command.CompletionFingerprintSha256))
            {
                return Result<RecordImplementationReviewResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.review_requires_completion_fingerprint",
                        "A review outcome requires fresh completion evidence confirming the checkpoint is still current."));
            }

            var reviewValidationError = ValidateReview(command.Outcome, command.Review);
            if (reviewValidationError is not null)
            {
                return Result<RecordImplementationReviewResultCommandResult>.Failure(reviewValidationError);
            }
        }
        else if (command.Review is not null)
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.review_requires_review_outcome",
                    "A validated review was supplied for an outcome other than ReviewApproved or ReviewChangesRequested."));
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Result<RecordImplementationReviewResultCommandResult>.Failure(
                Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Result<RecordImplementationReviewResultCommandResult>.Failure(
                    Error.Conflict(
                        "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Result<RecordImplementationReviewResultCommandResult>.Failure(
                    Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Result<RecordImplementationReviewResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.invalid_artifact_metadata", "One of the reported artifacts has malformed metadata."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        // CompleteAgent's own fingerprint-mismatch-to-SourceChanged override is authoritative: it
        // silently downgrades a review outcome to SourceChanged if fresh completion evidence no
        // longer matches this attempt's claimed checkpoint. The check below is against the
        // ATTEMPT'S OWN post-override outcome, never the caller's pre-override intent, so a review
        // is never appended for an attempt that drift silently invalidated.
        attempt.CompleteAgent(command.Outcome, command.CompletionFingerprintSha256, nowUtc);

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, nowUtc);
        }

        RunEvent latestEvent;
        if (attempt.AgentOutcome == AgentOutcome.ReviewApproved && command.Review is { IsApproved: true } approvedReview)
        {
            var claimedExecutions = await GetClaimedVerificationExecutionsAsync(attempt.Id, cancellationToken);
            var (reviewProjectId, reviewCheckpointNumber) = await GetReviewOwnershipAsync(attempt, cancellationToken);
            RecordCheckpointReview(attempt, reviewProjectId, reviewCheckpointNumber, ReviewDecision.Approved, claimedExecutions, nowUtc);

            latestEvent = RecordCollaborationMessage(
                run.Id,
                attempt.Id,
                executionReportMessageId,
                CollaborationMessageType.ReviewApproval,
                approvedReview.Summary,
                JsonSerializer.Serialize(new { rationale = approvedReview.Rationale, residualRisks = approvedReview.ResidualRisks }),
                nowUtc);
        }
        else if (attempt.AgentOutcome == AgentOutcome.ReviewChangesRequested && command.Review is { IsApproved: false } changesRequestedReview)
        {
            var claimedExecutions = await GetClaimedVerificationExecutionsAsync(attempt.Id, cancellationToken);
            var (reviewProjectId, reviewCheckpointNumber) = await GetReviewOwnershipAsync(attempt, cancellationToken);
            RecordCheckpointReview(attempt, reviewProjectId, reviewCheckpointNumber, ReviewDecision.ChangesRequested, claimedExecutions, nowUtc);

            latestEvent = null!;
            foreach (var finding in changesRequestedReview.Findings)
            {
                latestEvent = RecordCollaborationMessage(
                    run.Id,
                    attempt.Id,
                    executionReportMessageId,
                    CollaborationMessageType.ReviewFinding,
                    finding.Summary,
                    JsonSerializer.Serialize(new
                    {
                        severity = finding.Severity,
                        category = finding.Category,
                        evidence = finding.Evidence,
                        requiredChange = finding.RequiredChange,
                    }),
                    nowUtc);
            }
        }
        else
        {
            latestEvent = RunEvent.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                RunEventType.AgentAttemptCompleted,
                ParticipantKind.Orchestrator,
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome!.Value.ToString() }),
                nowUtc);
            dbContext.Events.Add(latestEvent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordImplementationReviewResultCommandResult>.Success(
            new RecordImplementationReviewResultCommandResult(attempt.Status, latestEvent.Sequence));
    }

    private static Error? ValidateReview(AgentOutcome outcome, ValidatedImplementationReview? review)
    {
        if (review is null)
        {
            return Error.Failure(
                "agent_attempts.review_requires_validated_review", "A review outcome requires a validated review.");
        }

        if (outcome == AgentOutcome.ReviewApproved)
        {
            if (!review.IsApproved)
            {
                return Error.Failure("agent_attempts.invalid_review_shape", "A ReviewApproved outcome requires an approved review.");
            }

            if (review.Findings.Count != 0)
            {
                return Error.Failure("agent_attempts.invalid_review_shape", "An approved review must not carry any findings.");
            }

            if (string.IsNullOrWhiteSpace(review.Rationale)
                || string.IsNullOrWhiteSpace(review.ResidualRisks)
                || !TryValidateContent(
                    CollaborationMessageType.ReviewApproval,
                    JsonSerializer.Serialize(new { rationale = review.Rationale, residualRisks = review.ResidualRisks })))
            {
                return Error.Failure("agent_attempts.invalid_review_content", "The approval failed content policy validation.");
            }

            return null;
        }

        // outcome == AgentOutcome.ReviewChangesRequested
        if (review.IsApproved)
        {
            return Error.Failure("agent_attempts.invalid_review_shape", "A ReviewChangesRequested outcome requires a non-approved review.");
        }

        if (review.Findings.Count is < ImplementationReviewOutputSchema.MinimumFindings or > ImplementationReviewOutputSchema.MaximumFindings)
        {
            return Error.Failure("agent_attempts.invalid_review_shape", "A changes-requested review must carry one to ten findings.");
        }

        foreach (var finding in review.Findings)
        {
            if (!CollaborationMessageContentPolicy.IsSafeSummary(finding.Summary)
                || !TryValidateContent(
                    CollaborationMessageType.ReviewFinding,
                    JsonSerializer.Serialize(new
                    {
                        severity = finding.Severity,
                        category = finding.Category,
                        evidence = finding.Evidence,
                        requiredChange = finding.RequiredChange,
                    })))
            {
                return Error.Failure("agent_attempts.invalid_review_content", "One of the findings failed content policy validation.");
            }
        }

        return null;
    }

    private static bool TryValidateContent(CollaborationMessageType type, string structuredContentJson)
    {
        try
        {
            CollaborationMessageContentPolicy.Validate(type, structuredContentJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches this attempt's durably claimed, ordered verification-execution set (recorded at
    /// claim time by <c>CreateCodeReviewAttemptCommandHandler</c>, before the provider was ever
    /// invoked) — never re-selected or re-evaluated now: a <see cref="VerificationExecution"/> is
    /// immutable once terminal, so the claimed set's status can never have changed since claim
    /// time.
    /// </summary>
    private async Task<IReadOnlyList<VerificationExecution>> GetClaimedVerificationExecutionsAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var claimedEvidence = await dbContext.AttemptVerificationEvidence
            .Where(evidence => evidence.AttemptId == attemptId)
            .OrderBy(evidence => evidence.Sequence)
            .ToListAsync(cancellationToken);

        var executionIds = claimedEvidence.Select(evidence => evidence.VerificationExecutionId).ToArray();
        var executionsById = await dbContext.VerificationExecutions
            .Where(execution => executionIds.Contains(execution.Id))
            .ToDictionaryAsync(execution => execution.Id, cancellationToken);

        return claimedEvidence.Select(claim => executionsById[claim.VerificationExecutionId]).ToArray();
    }

    private async Task<(Guid ProjectId, int CheckpointNumber)> GetReviewOwnershipAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(workspace => workspace.Id == attempt.AgentGitWorkspaceId!.Value)
            .Select(workspace => workspace.ProjectId)
            .SingleAsync(cancellationToken);
        var checkpointNumber = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(checkpoint => checkpoint.Id == attempt.AgentGitCheckpointId!.Value)
            .Select(checkpoint => checkpoint.CheckpointNumber)
            .SingleAsync(cancellationToken);

        return (projectId, checkpointNumber);
    }

    private void RecordCheckpointReview(
        Attempt attempt,
        Guid projectId,
        int checkpointNumber,
        ReviewDecision decision,
        IReadOnlyList<VerificationExecution> claimedExecutions,
        DateTimeOffset nowUtc)
    {
        var reviewId = Guid.NewGuid();

        var evidenceMembers = claimedExecutions
            .Select(execution => CheckpointReviewEvidence.Observe(
                Guid.NewGuid(),
                reviewId,
                execution.VerificationCommandId,
                execution.Id,
                execution.ExecutionNumber,
                execution.CheckpointFingerprintSha256,
                execution.Status,
                execution.Outcome,
                execution.ExitCode))
            .ToArray();

        var review = CheckpointReview.Record(
            reviewId,
            projectId,
            attempt.AgentGitWorkspaceId!.Value,
            attempt.AgentGitCheckpointId!.Value,
            checkpointNumber,
            attempt.AgentCheckpointFingerprintSha256!,
            ReviewActorKind.FutureAgent,
            decision,
            nowUtc,
            evidenceMembers);

        dbContext.CheckpointReviews.Add(review);
        dbContext.CheckpointReviewEvidence.AddRange(evidenceMembers);
    }

    private RunEvent RecordCollaborationMessage(
        Guid runId,
        Guid attemptId,
        Guid inReplyToMessageId,
        CollaborationMessageType type,
        string summary,
        string structuredContentJson,
        DateTimeOffset nowUtc)
    {
        var message = CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            attemptId,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex,
            ParticipantKind.Claude,
            type,
            inReplyToMessageId,
            summary,
            structuredContentJson,
            CollaborationMessageProvenance.ProviderObserved,
            nowUtc);
        dbContext.CollaborationMessages.Add(message);

        var runEvent = RunEvent.Record(
            Guid.NewGuid(),
            runId,
            attemptId,
            RunEventType.CollaborationMessageRecorded,
            ParticipantKind.Codex,
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

    private void RecordArtifact(Guid runId, Guid attemptId, SealedImplementationReviewArtifact sealedArtifact, DateTimeOffset nowUtc)
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
